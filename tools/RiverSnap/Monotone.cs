namespace RiverSnap
{
    /// <summary>
    /// The least-absolute-deviation fit of a sequence that never rises downstream (L1 isotonic regression, by the "slope
    /// trick": a max-heap of the values seen from the mouth upstream, then a backward pass). A bump shorter than the river
    /// it blocks is cut through and a pit shorter than the water around it is filled, whichever moves fewer stations by
    /// fewer metres: the hybrid of breaching and filling that hydrological DEM conditioning uses.
    /// </summary>
    static class L1Fit
    {
        /// <summary>Fits values[from..from+count) (flow order, source first) into fit[from..]: never rising downstream.
        /// NaN values take the last value upstream that is not NaN (or the first one after them).</summary>
        public static void Fit(double[] values, int from, int count, double[] fit, PriorityQueue<double, double> heap, double[] top)
        {
            if (count <= 0) return;
            heap.Clear();
            // upstream along the chain: from the mouth (index count-1) to the source, the fit must not fall
            double last = double.NaN;
            for (int i = 0; i < count && double.IsNaN(last); i++) last = values[from + i];
            if (double.IsNaN(last)) { for (int i = 0; i < count; i++) fit[from + i] = double.NaN; return; }
            // NaN values: the value upstream of them
            for (int k = 0; k < count; k++)
            {
                int i = from + count - 1 - k;
                double x = values[i];
                if (double.IsNaN(x))
                {
                    // nearest valid value upstream (towards the source), else downstream
                    int u = i - 1;
                    while (u >= from && double.IsNaN(values[u])) u--;
                    x = u >= from ? values[u] : last;
                }
                // max-heap by priority -x
                heap.Enqueue(x, -x);
                double mx = heap.Peek();
                if (mx > x)
                {
                    heap.Dequeue();
                    heap.Enqueue(x, -x);
                }
                top[k] = heap.Peek();
            }
            // backward: b[last] = top[last], b[k] = min(top[k], b[k+1]); k counts from the mouth
            double b = top[count - 1];
            fit[from] = b;
            for (int k = count - 2; k >= 0; k--)
            {
                b = Math.Min(top[k], b);
                fit[from + count - 1 - k] = b;
            }
        }
    }
}
