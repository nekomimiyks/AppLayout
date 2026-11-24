using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace AppLayout
{
    internal static class DisplayTopology
    {
        internal static string Key(IList<MonitorDesc> monitors)
        {
            if (monitors.Count == 0 || monitors.Count(m => m.Primary) != 1 ||
                monitors.Any(m => m.Bounds.Left >= m.Bounds.Right || m.Bounds.Top >= m.Bounds.Bottom)) return null;

            // 移行先でも同じ配置を選べるよう、機器IDや Windows のモニター番号は使わない。
            var rectangles = monitors.Select(m => string.Format(CultureInfo.InvariantCulture, "{0},{1},{2},{3},{4}",
                m.Bounds.Left, m.Bounds.Top, m.Bounds.Right, m.Bounds.Bottom, m.Primary ? 1 : 0));
            return string.Join(";", rectangles.OrderBy(r => r, StringComparer.Ordinal));
        }
    }

    internal sealed class DisplayDetection
    {
        readonly ConfigStore store;
        const long SettleMilliseconds = 1500;
        string candidate;
        int candidateSignal, acceptedSignal;
        long candidateSince;
        bool ready;

        internal DisplayDetection(ConfigStore store) { this.store = store; }
        internal bool IsReady(int signal) { return ready && acceptedSignal == signal; }

        internal bool Observe(string key, int signal, long now)
        {
            if (key != null && IsReady(signal) && store.ActiveDisplay != null && store.ActiveDisplay.Key == key) return true;
            ready = false;
            if (key == null || candidate != key || candidateSignal != signal)
            {
                candidate = key;
                candidateSignal = signal;
                candidateSince = now;
                return false;
            }
            if (now - candidateSince < SettleMilliseconds) return false;

            store.SelectDisplay(key);
            acceptedSignal = signal;
            ready = true;
            return true;
        }
    }
}
