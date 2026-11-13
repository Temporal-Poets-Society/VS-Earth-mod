# Initial mountain profile server checks

Date: 2026-10-26 | Vintage Story 1.22.7 | Cylinder World 0.11.14 | Vanilla ecology providers

These development runs preceded the complete continental Release matrix. The initial minimal server harness lacked
a player role; that harness configuration was corrected before the successful runs below. The K512-smoke2 report is
from a reopen after changing the creation controls to legacy: the saved profile still remained 128/balanced/half,
with local ecology and physical rock. These runs are integration evidence, not a controlled performance benchmark.
They used intermediate Debug builds of the same experimental version; exact Release evidence is recorded by the
continental matrix's assembly hash.

## mountain-K512-smoke2

```text
world 512000 x 204800 x 512, sea level 128
terrain profile: 128/balanced/half; curve=6000; detail=0.5; ecology=True; rock=True; climate=2; API sea=128; helper sea=128
vertical sea agreement: PASS; Earth=128, Cylinder=128
vertical climate 35.36,138.73: helper=-2.62, Earth=-2.40, PASS
vertical climate 27.99,86.93: helper=-17.76, Earth=-17.72, PASS
vertical climate 9.03,38.74: helper=16.12, Earth=16.18, PASS
vertical climate 56.95,24.1: helper=6.59, Earth=6.79, PASS
vertical climate helpers: PASS
35.36,138.73 at 35.36°N 138.74°E: 9216 columns in 0.1s; terrain height = model in 99.4% (within 2: 99.6%, max diff 24); terrain y min/10%/50%/90%/max 198/224/247/272/278; surface y 198..286; water 0%; top blocks: soil 59%, leaves 20%, forestfloor 17%, leavesbranchy 2%, fern 1%, air 0%, crop 0%, flower 0%
27.99,86.93 at 27.98°N 86.92°E: 9216 columns in 0.1s; terrain height = model in 100.0% (within 2: 100.0%, max diff 0); terrain y min/10%/50%/90%/max 377/392/415/428/435; surface y 377..436; water 0%; top blocks: snowlayer 64%, snowblock 31%, rock 5%, looseboulders 0%, crackedrock 0%
-15.97,-5.71 at 15.98°S 5.70°W: 9216 columns in 0.1s; terrain height = model in 99.9% (within 2: 100.0%, max diff 2); terrain y min/10%/50%/90%/max 97/122/149/186/194; surface y 127..194; water 21%; top blocks: sand 78%, saltwater 21%, looseboulders 1%, leaves 0%, leavesbranchy 0%, bonyremains 0%
```

## mountain-K384-smoke

```text
world 512000 x 204800 x 384, sea level 128
terrain profile: 128/balanced/half; curve=6000; detail=0.5; ecology=True; rock=True; climate=2; API sea=128; helper sea=128
vertical sea agreement: PASS; Earth=128, Cylinder=128
vertical climate 35.36,138.73: helper=-2.65, Earth=-2.43, PASS
vertical climate 27.99,86.93: helper=-17.54, Earth=-17.50, PASS
vertical climate 9.03,38.74: helper=16.20, Earth=16.26, PASS
vertical climate 56.95,24.1: helper=6.59, Earth=6.79, PASS
vertical climate helpers: PASS
35.36,138.73 at 35.36°N 138.74°E: 9216 columns in 3.6s; terrain height = model in 99.7% (within 2: 99.8%, max diff 7); terrain y min/10%/50%/90%/max 174/191/206/223/226; surface y 174..237; water 0%; top blocks: soil 58%, leaves 20%, forestfloor 19%, leavesbranchy 2%, fern 1%, air 0%, crop 0%, flower 0%
27.99,86.93 at 27.98°N 86.92°E: 9216 columns in 4.6s; terrain height = model in 100.0% (within 2: 100.0%, max diff 0); terrain y min/10%/50%/90%/max 292/302/317/326/330; surface y 292..331; water 0%; top blocks: snowlayer 64%, snowblock 36%, rock 0%, crackedrock 0%
-15.97,-5.71 at 15.98°S 5.70°W: 9216 columns in 2.2s; terrain height = model in 99.7% (within 2: 99.8%, max diff 8); terrain y min/10%/50%/90%/max 97/122/142/166/172; surface y 125..172; water 21%; top blocks: sand 78%, saltwater 21%, looseboulders 1%, leaves 0%, leavesbranchy 0%, bonyremains 0%
```

## mountain-A384-smoke

```text
world 512000 x 204800 x 384, sea level 165
terrain profile: legacy/legacy/normal; curve=200; detail=1; ecology=False; rock=False; climate=1; API sea=165; helper sea=165
vertical sea agreement: PASS; Earth=165, Cylinder=165
35.36,138.73 at 35.36°N 138.74°E: 9216 columns in 3.7s; terrain height = model in 100.0% (within 2: 100.0%, max diff 0); terrain y min/10%/50%/90%/max 258/280/289/302/305; surface y 258..323; water 0%; top blocks: soil 44%, forestfloor 27%, leaves 22%, rock 2%, leavesbranchy 2%, fern 1%, water 0%, crop 0%
27.99,86.93 at 27.98°N 86.92°E: 9216 columns in 4.4s; terrain height = model in 100.0% (within 2: 100.0%, max diff 0); terrain y min/10%/50%/90%/max 334/337/343/347/348; surface y 334..349; water 0%; top blocks: snowlayer 70%, snowblock 28%, rock 1%, crackedrock 1%, looseboulders 0%, soil 0%
-15.97,-5.71 at 15.98°S 5.70°W: 9216 columns in 2.2s; terrain height = model in 99.9% (within 2: 100.0%, max diff 4); terrain y min/10%/50%/90%/max 119/159/215/255/261; surface y 164..262; water 21%; top blocks: rock 53%, saltwater 21%, gravel 16%, sand 6%, crackedrock 3%, looseboulders 1%, bonyremains 0%, ore 0%
```

## mountain-B512-full

```text
world 512000 x 204800 x 512, sea level 128
terrain profile: 128/legacy/normal; curve=200; detail=1; ecology=False; rock=False; climate=2; API sea=128; helper sea=128
vertical sea agreement: PASS; Earth=128, Cylinder=128
vertical climate 35.36,138.73: helper=-2.64, Earth=-2.42, PASS
vertical climate 27.99,86.93: helper=-17.45, Earth=-17.42, PASS
vertical climate 9.03,38.74: helper=16.14, Earth=16.20, PASS
vertical climate 56.95,24.1: helper=6.59, Earth=6.79, PASS
vertical climate helpers: PASS
35.36,138.73 at 35.36°N 138.74°E: 9216 columns in 4.4s; terrain height = model in 99.5% (within 2: 99.7%, max diff 14); terrain y min/10%/50%/90%/max 296/336/352/375/381; surface y 296..396; water 0%; top blocks: soil 39%, rock 20%, leaves 19%, forestfloor 19%, leavesbranchy 2%, fern 1%, water 0%, crackedrock 0%
27.99,86.93 at 27.98°N 86.92°E: 9216 columns in 5.8s; terrain height = model in 97.9% (within 2: 98.9%, max diff 5); terrain y min/10%/50%/90%/max 434/439/450/456/459; surface y 434..460; water 0%; top blocks: snowlayer 50%, snowblock 48%, rock 2%, air 0%, looseboulders 0%, soil 0%, ore 0%
-15.97,-5.71 at 15.98°S 5.70°W: 9216 columns in 3.2s; terrain height = model in 99.7% (within 2: 99.9%, max diff 6); terrain y min/10%/50%/90%/max 97/122/219/291/302; surface y 127..302; water 21%; top blocks: rock 69%, saltwater 21%, sand 8%, crackedrock 1%, looseboulders 1%, ore 0%, bonyremains 0%
36.40,25.40 at 36.40°N 25.39°E: 9216 columns in 3.9s; terrain height = model in 99.9% (within 2: 99.9%, max diff 5); terrain y min/10%/50%/90%/max 107/118/124/131/185; surface y 127..185; water 75%; top blocks: saltwater 75%, sand 13%, rock 11%, crackedrock 0%, ore 0%, leaves 0%, looseboulders 0%, flower 0%
9.03,38.74 at 9.03°N 38.74°E: 9216 columns in 5.2s; terrain height = model in 100.0% (within 2: 100.0%, max diff 0); terrain y min/10%/50%/90%/max 359/361/364/372/375; surface y 359..389; water 0%; top blocks: soil 73%, forestfloor 13%, leaves 8%, gravel 5%, peat 1%, crop 1%, flower 1%, water 0%
56.95,24.10 at 56.95°N 24.09°E: 9216 columns in 5.6s; terrain height = model in 99.8% (within 2: 99.9%, max diff 8); terrain y min/10%/50%/90%/max 120/120/128/130/132; surface y 120..149; water 23%; top blocks: soil 46%, water 23%, leaves 12%, sand 8%, forestfloor 8%, fern 1%, leavesbranchy 0%, crop 0%
jumpline at 65.80°N 169.25°W: 9216 columns in 6.0s; terrain height = model in 0.0% (within 2: 0.0%, max diff 34); terrain y min/10%/50%/90%/max 83/90/95/98/100; surface y 127..127; water 100%; top blocks: saltwater 100%
-85.5,20.0 at 85.51°S 19.99°E: 9216 columns in 5.2s; terrain height = model in 0.0% (within 2: 0.0%, max diff 236); terrain y min/10%/50%/90%/max 139/144/154/194/233; surface y 140..234; water 0%; top blocks: snowlayer 100%
```

## mountain-E512-smoke

```text
world 512000 x 204800 x 512, sea level 128
terrain profile: 128/linear/normal; curve=linear; detail=1; ecology=False; rock=False; climate=2; API sea=128; helper sea=128
vertical sea agreement: PASS; Earth=128, Cylinder=128
vertical climate 35.36,138.73: helper=-2.54, Earth=-2.32, PASS
vertical climate 27.99,86.93: helper=-17.74, Earth=-17.71, PASS
vertical climate 9.03,38.74: helper=16.08, Earth=16.14, PASS
vertical climate 56.95,24.1: helper=6.59, Earth=6.79, PASS
vertical climate helpers: PASS
35.36,138.73 at 35.36°N 138.74°E: 9216 columns in 3.8s; terrain height = model in 99.7% (within 2: 99.9%, max diff 4); terrain y min/10%/50%/90%/max 169/195/210/236/244; surface y 169..257; water 0%; top blocks: soil 43%, rock 19%, forestfloor 19%, leaves 17%, fern 1%, leavesbranchy 1%, crackedrock 1%, crop 0%
27.99,86.93 at 27.98°N 86.92°E: 9216 columns in 5.0s; terrain height = model in 100.0% (within 2: 100.0%, max diff 0); terrain y min/10%/50%/90%/max 339/353/382/399/408; surface y 339..409; water 0%; top blocks: snowlayer 53%, snowblock 32%, rock 14%, glacierice 1%, crackedrock 0%
-15.97,-5.71 at 15.98°S 5.70°W: 9216 columns in 2.9s; terrain height = model in 100.0% (within 2: 100.0%, max diff 0); terrain y min/10%/50%/90%/max 97/122/141/166/172; surface y 127..173; water 21%; top blocks: sand 63%, saltwater 21%, rock 14%, leaves 1%, looseboulders 1%, leavesbranchy 0%, bonyremains 0%
```

## mountain-G384-smoke

```text
world 512000 x 204800 x 384, sea level 110
terrain profile: 110/balanced/normal; curve=6000; detail=1; ecology=False; rock=False; climate=2; API sea=110; helper sea=110
vertical sea agreement: PASS; Earth=110, Cylinder=110
vertical climate 35.36,138.73: helper=-2.51, Earth=-2.29, PASS
vertical climate 27.99,86.93: helper=-17.61, Earth=-17.58, PASS
vertical climate 9.03,38.74: helper=16.13, Earth=16.19, PASS
vertical climate 56.95,24.1: helper=6.59, Earth=6.79, PASS
vertical climate helpers: PASS
35.36,138.73 at 35.36°N 138.74°E: 9216 columns in 3.4s; terrain height = model in 99.6% (within 2: 99.7%, max diff 32); terrain y min/10%/50%/90%/max 149/178/191/213/219; surface y 149..233; water 0%; top blocks: soil 43%, forestfloor 22%, leaves 20%, rock 13%, fern 1%, leavesbranchy 1%, water 0%, air 0%
27.99,86.93 at 27.98°N 86.92°E: 9216 columns in 4.6s; terrain height = model in 100.0% (within 2: 100.0%, max diff 0); terrain y min/10%/50%/90%/max 285/293/310/319/324; surface y 285..325; water 0%; top blocks: snowlayer 52%, snowblock 46%, rock 2%, crackedrock 0%, soil 0%
-15.97,-5.71 at 15.98°S 5.70°W: 9216 columns in 2.1s; terrain height = model in 99.2% (within 2: 99.5%, max diff 22); terrain y min/10%/50%/90%/max 79/104/125/151/157; surface y 109..158; water 21%; top blocks: sand 58%, saltwater 21%, rock 16%, crackedrock 3%, leaves 2%, looseboulders 1%, leavesbranchy 0%, bonyremains 0%
```

## mountain-K512-full

```text
world 512000 x 204800 x 512, sea level 128
terrain profile: 128/balanced/half; curve=6000; detail=0.5; ecology=True; rock=True; climate=2; API sea=128; helper sea=128
vertical sea agreement: PASS; Earth=128, Cylinder=128
vertical climate 35.36,138.73: helper=-2.62, Earth=-2.40, PASS
vertical climate 27.99,86.93: helper=-17.76, Earth=-17.72, PASS
vertical climate 9.03,38.74: helper=16.12, Earth=16.18, PASS
vertical climate 56.95,24.1: helper=6.59, Earth=6.79, PASS
vertical climate helpers: PASS
35.36,138.73 at 35.36°N 138.74°E: 9216 columns in 4.3s; terrain height = model in 99.4% (within 2: 99.6%, max diff 24); terrain y min/10%/50%/90%/max 198/224/247/272/278; surface y 198..286; water 0%; top blocks: soil 59%, leaves 20%, forestfloor 17%, leavesbranchy 2%, fern 1%, air 0%, crop 0%, flower 0%
27.99,86.93 at 27.98°N 86.92°E: 9216 columns in 5.6s; terrain height = model in 100.0% (within 2: 100.0%, max diff 0); terrain y min/10%/50%/90%/max 377/392/415/428/435; surface y 377..436; water 0%; top blocks: snowlayer 64%, snowblock 31%, rock 5%, looseboulders 0%, crackedrock 0%
-15.97,-5.71 at 15.98°S 5.70°W: 9216 columns in 2.8s; terrain height = model in 99.9% (within 2: 100.0%, max diff 2); terrain y min/10%/50%/90%/max 97/122/149/186/194; surface y 127..194; water 21%; top blocks: sand 78%, saltwater 21%, looseboulders 1%, leaves 0%, leavesbranchy 0%, bonyremains 0%
36.40,25.40 at 36.40°N 25.39°E: 9216 columns in 3.5s; terrain height = model in 100.0% (within 2: 100.0%, max diff 0); terrain y min/10%/50%/90%/max 107/118/124/128/138; surface y 127..139; water 75%; top blocks: saltwater 75%, sand 25%, leaves 0%, looseboulders 0%, flower 0%
9.03,38.74 at 9.03°N 38.74°E: 9216 columns in 4.3s; terrain height = model in 100.0% (within 2: 100.0%, max diff 0); terrain y min/10%/50%/90%/max 252/254/258/268/272; surface y 252..285; water 0%; top blocks: soil 72%, forestfloor 13%, leaves 8%, gravel 4%, peat 1%, crop 1%, flower 1%, fruitingbush 0%
56.95,24.10 at 56.95°N 24.09°E: 9216 columns in 4.8s; terrain height = model in 99.3% (within 2: 99.9%, max diff 8); terrain y min/10%/50%/90%/max 120/120/128/128/128; surface y 120..148; water 23%; top blocks: soil 44%, water 23%, leaves 13%, sand 9%, forestfloor 8%, fern 1%, leavesbranchy 0%, crop 0%
jumpline at 65.80°N 169.25°W: 9216 columns in 6.3s; terrain height = model in 0.0% (within 2: 0.0%, max diff 34); terrain y min/10%/50%/90%/max 83/90/95/98/100; surface y 127..127; water 100%; top blocks: saltwater 100%
-85.5,20.0 at 85.51°S 19.99°E: 9216 columns in 4.5s; terrain height = model in 0.0% (within 2: 0.0%, max diff 134); terrain y min/10%/50%/90%/max 139/144/154/194/233; surface y 140..234; water 0%; top blocks: snowlayer 100%
```
