using System;
using UnityEngine;

namespace Game.Client
{
    // How smoothly the game runs where it is played (Docs/WebDemo.md 성능): frame times, garbage collections and
    // memory taken per frame since the last reset. The playtests read it ("perf", "perf reset") on each screen.
    public sealed partial class GameRoot
    {
        static readonly float[] FrameMs = new float[600];
        int _perfFrames;
        long _perfAllocated;
        long _perfLastMemory;
        int _perfCollections;

        void ResetPerf()
        {
            _perfFrames = 0;
            _perfAllocated = 0;
            _perfLastMemory = GC.GetTotalMemory(false);
            _perfCollections = GC.CollectionCount(0);
        }

        void TrackPerf(float dt)
        {
            FrameMs[_perfFrames % FrameMs.Length] = dt * 1000f;
            _perfFrames++;
            // Memory that grew since the last frame was taken by this one (a collection in between hides a little).
            long memory = GC.GetTotalMemory(false);
            if (memory > _perfLastMemory) _perfAllocated += memory - _perfLastMemory;
            _perfLastMemory = memory;
        }

        // "frames 180 · avg 16.7 ms · p95 18.0 ms · max 25.1 ms · gc 0 · alloc 1.2 KB/frame · heap 24.0 MB" as JSON.
        string PerfReport()
        {
            int n = Mathf.Min(_perfFrames, FrameMs.Length);
            if (n == 0) return "{\"frames\":0}";
            var times = new float[n];
            Array.Copy(FrameMs, times, n);
            Array.Sort(times);
            float sum = 0;
            foreach (float t in times) sum += t;
            return new Json().Open()
                .Add("frames", _perfFrames)
                .Add("avgMs", Mathf.Round(sum / n * 10) / 10)
                .Add("p95Ms", Mathf.Round(times[Mathf.Min(n - 1, (int)(n * 0.95f))] * 10) / 10)
                .Add("maxMs", Mathf.Round(times[n - 1] * 10) / 10)
                .Add("gc", GC.CollectionCount(0) - _perfCollections)
                .Add("allocKbPerFrame", Mathf.Round(_perfAllocated / 1024f / Mathf.Max(1, _perfFrames) * 10) / 10)
                .Add("heapMb", Mathf.Round(GC.GetTotalMemory(false) / 1048576f * 10) / 10)
                .Close().ToString();
        }
    }
}
