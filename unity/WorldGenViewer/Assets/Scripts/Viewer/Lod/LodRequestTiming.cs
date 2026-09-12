using System;

namespace WorldGen.Viewer.Lod
{
    /// <summary>ND-95: kizárólag diagnosztika; nem Unity-frame-óra és nem CPU-idő.</summary>
    public readonly struct LodRequestTiming
    {
        public double QueueMs { get; }
        public double WorkerMs { get; }
        public double ReadyWaitMs { get; }
        public double StagingWallMs { get; }
        public double CommitMs { get; }
        public double TotalMs { get; }

        public LodRequestTiming(long requested, long workerStarted, long workerReady,
            long observed, long commitStarted, long committed, long frequency)
        {
            if (frequency <= 0) throw new ArgumentOutOfRangeException(nameof(frequency));
            if (workerStarted < requested || workerReady < workerStarted || observed < workerReady
                || commitStarted < observed || committed < commitStarted)
                throw new ArgumentException("A kérés időbélyegei nem időrendben vannak.");
            QueueMs = (workerStarted - requested) * 1000.0 / frequency;
            WorkerMs = (workerReady - workerStarted) * 1000.0 / frequency;
            ReadyWaitMs = (observed - workerReady) * 1000.0 / frequency;
            StagingWallMs = (commitStarted - observed) * 1000.0 / frequency;
            CommitMs = (committed - commitStarted) * 1000.0 / frequency;
            TotalMs = (committed - requested) * 1000.0 / frequency;
        }
    }
}
