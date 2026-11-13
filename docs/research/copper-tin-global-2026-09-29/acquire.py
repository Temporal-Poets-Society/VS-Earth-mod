"""Read-only retrieval of public geological sources; run separately from normalization."""
from pathlib import Path
import concurrent.futures, datetime, hashlib, json, urllib.request

ROOT = Path(__file__).resolve().parent
SOURCES = {
    'copper-assessment-gis.zip': 'https://pubs.usgs.gov/sir/2010/5090/z/sir20105090z_gis.zip',
    'copper-assessment-report.pdf': 'https://pubs.usgs.gov/sir/2010/5090/z/sir20105090z.pdf',
    'mrds-csv.zip': 'https://mrdata.usgs.gov/mrds/mrds-csv.zip',
    'mrds-metadata.xml': 'https://mrdata.usgs.gov/metadata/mrds.xml',
    'mrds-about.html': 'https://mrdata.usgs.gov/mrds/about.php',
    'sedcu-csv.zip': 'https://mrdata.usgs.gov/sedcu/sedcu-csv.zip',
    'sedcu-metadata.xml': 'https://mrdata.usgs.gov/metadata/ofr-2003-of03-107.xml',
    'gsc-tin-tungsten-report.pdf': 'https://publications.gc.ca/collections/collection_2016/rncan-nrcan/M183-2-7688-eng.pdf',
    'gsc-doi-metadata.json': 'https://api.datacite.org/dois/10.4095/295581',
    'porphyry-2025-doi-metadata.json': 'https://api.datacite.org/dois/10.5066/P14CCESQ',
}

def acquire(item):
    name, url = item
    path = ROOT / 'raw' / name
    path.parent.mkdir(parents=True, exist_ok=True)
    result = {'file': str(path.relative_to(ROOT)), 'url': url, 'retrieved_utc': datetime.datetime.now(datetime.timezone.utc).isoformat()}
    if path.exists():
        result['status'] = 'already_present'
    else:
        try:
            with urllib.request.urlopen(url, timeout=50) as response:
                content = response.read()
                result.update(final_url=response.url, content_type=response.headers.get('Content-Type'), last_modified=response.headers.get('Last-Modified'))
            if (path.suffix == '.pdf' and not content.startswith(b'%PDF')) or (path.suffix == '.zip' and not content.startswith(b'PK')):
                path = path.with_name(path.stem + '-response.html')
                path.write_bytes(content)
                result.update(file=str(path.relative_to(ROOT)), status='unexpected_content_not_dataset',
                              bytes=len(content),sha256=hashlib.sha256(content).hexdigest())
                print(json.dumps(result),flush=True)
                return result
            path.write_bytes(content)
            result['status'] = 'downloaded'
        except Exception as error:
            result.update(status='failed', error=str(error))
    if path.exists():
        content = path.read_bytes()
        result.update(bytes=len(content), sha256=hashlib.sha256(content).hexdigest())
    print(json.dumps(result), flush=True)
    return result

if __name__ == '__main__':
    with concurrent.futures.ThreadPoolExecutor(max_workers=4) as pool:
        records = list(pool.map(acquire, SOURCES.items()))
    (ROOT / 'download-manifest.json').write_text(json.dumps(records, indent=2), encoding='utf-8')
