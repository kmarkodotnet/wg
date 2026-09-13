#nullable enable
using System;
using System.Collections.Generic;

namespace WorldGen.App.Loading
{
    public sealed class LoadingStage
    {
        public LoadingStage(string id, string messageKey, double weight, bool isCancellable = true)
        {
            if (string.IsNullOrEmpty(id)) throw new ArgumentException("Üres szakasz-azonosító.", nameof(id));
            if (!double.IsFinite(weight) || weight <= 0) throw new ArgumentOutOfRangeException(nameof(weight));
            Id = id;
            MessageKey = messageKey ?? "";
            Weight = weight;
            IsCancellable = isCancellable;
        }

        public string Id { get; }
        public string MessageKey { get; }

        /// <summary>A szakasz becsült relatív költsége (nem kell fizikailag pontosnak lennie, de ne legyen félrevezető).</summary>
        public double Weight { get; }

        public bool IsCancellable { get; }
    }

    public readonly struct LoadingProgressSnapshot
    {
        public LoadingProgressSnapshot(double overall, int stageIndex, int stageCount, string? stageId, string? messageKey,
            bool isCancellable, bool isCompleted, string? failureMessage)
        {
            Overall = overall;
            StageIndex = stageIndex;
            StageCount = stageCount;
            StageId = stageId;
            MessageKey = messageKey;
            IsCancellable = isCancellable;
            IsCompleted = isCompleted;
            FailureMessage = failureMessage;
        }

        /// <summary>0..1, monoton nem csökkenő.</summary>
        public double Overall { get; }

        /// <summary>-1 a kezdés előtt.</summary>
        public int StageIndex { get; }

        public int StageCount { get; }
        public string? StageId { get; }
        public string? MessageKey { get; }
        public bool IsCancellable { get; }
        public bool IsCompleted { get; }
        public string? FailureMessage { get; }
        public bool IsFailed => FailureMessage != null;
    }

    /// <summary>
    /// Súlyozott, szálbiztos betöltési progress (WF-LOAD-001/002). A generáló
    /// szál jelent, a UI-szál pillanatképet olvas. A szakaszok csak előre
    /// léphetnek (a kihagyott szakasz késznek számít), az összesített arány nem csökken.
    /// </summary>
    public sealed class LoadingProgressTracker
    {
        private readonly object _gate = new object();
        private readonly List<LoadingStage> _stages;
        private readonly double _totalWeight;
        private int _index = -1;
        private double _stageFraction;
        private bool _completed;
        private string? _failure;

        public LoadingProgressTracker(IEnumerable<LoadingStage> stages)
        {
            if (stages == null) throw new ArgumentNullException(nameof(stages));
            _stages = new List<LoadingStage>(stages);
            if (_stages.Count == 0) throw new ArgumentException("Legalább egy szakasz kell.", nameof(stages));
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var s in _stages)
            {
                if (s == null || !ids.Add(s.Id)) throw new ArgumentException("Null vagy duplikált szakasz.", nameof(stages));
                _totalWeight += s.Weight;
            }
        }

        public IReadOnlyList<LoadingStage> Stages => _stages;

        public void BeginStage(string stageId)
        {
            lock (_gate)
            {
                EnsureRunning();
                int target = IndexOf(stageId);
                if (target < 0) throw new ArgumentException("Ismeretlen szakasz: " + stageId, nameof(stageId));
                if (target < _index) throw new InvalidOperationException("A betöltési szakasz nem léphet vissza: " + stageId);
                if (target == _index) return;
                _index = target;
                _stageFraction = 0;
            }
        }

        /// <summary>Az aktuális szakasz készültsége [0, 1]-ben; csökkenő vagy NaN jelentés hatástalan.</summary>
        public void ReportStageProgress(double fraction)
        {
            lock (_gate)
            {
                if (_index < 0 || _completed || _failure != null || double.IsNaN(fraction)) return;
                double f = fraction < 0 ? 0 : fraction > 1 ? 1 : fraction;
                if (f > _stageFraction) _stageFraction = f;
            }
        }

        public void CompleteStage() => ReportStageProgress(1);

        public void Complete()
        {
            lock (_gate)
            {
                if (_failure != null) return;
                _index = _stages.Count - 1;
                _stageFraction = 1;
                _completed = true;
            }
        }

        public void Fail(string message)
        {
            lock (_gate)
            {
                if (_completed) return;
                _failure = string.IsNullOrEmpty(message) ? "unknown" : message;
            }
        }

        public LoadingProgressSnapshot Snapshot()
        {
            lock (_gate)
            {
                if (_index < 0)
                    return new LoadingProgressSnapshot(0, -1, _stages.Count, null, null, true, false, _failure);

                double done = 0;
                for (int i = 0; i < _index; i++) done += _stages[i].Weight;
                done += _stages[_index].Weight * _stageFraction;
                double overall = _completed ? 1.0 : Math.Min(1.0, done / _totalWeight);
                var stage = _stages[_index];
                return new LoadingProgressSnapshot(overall, _index, _stages.Count, stage.Id, stage.MessageKey,
                    stage.IsCancellable && !_completed, _completed, _failure);
            }
        }

        private void EnsureRunning()
        {
            if (_completed) throw new InvalidOperationException("A betöltés már befejeződött.");
            if (_failure != null) throw new InvalidOperationException("A betöltés már hibával leállt.");
        }

        private int IndexOf(string id)
        {
            for (int i = 0; i < _stages.Count; i++)
                if (string.Equals(_stages[i].Id, id, StringComparison.Ordinal)) return i;
            return -1;
        }
    }

    /// <summary>
    /// A kijelzett progress simítása: exponenciálisan közelít a valódihoz, de
    /// soha nem előzi meg és soha nem lép vissza (nem félrevezető, WF-LOAD-001).
    /// </summary>
    public sealed class ProgressDisplaySmoother
    {
        public ProgressDisplaySmoother(double responsiveness = 6.0)
        {
            if (!double.IsFinite(responsiveness) || responsiveness <= 0) throw new ArgumentOutOfRangeException(nameof(responsiveness));
            Responsiveness = responsiveness;
        }

        public double Responsiveness { get; }
        public double Displayed { get; private set; }

        public double Update(double actual, double unscaledDeltaSeconds)
        {
            if (double.IsNaN(actual)) return Displayed;
            double target = actual < 0 ? 0 : actual > 1 ? 1 : actual;
            if (target <= Displayed || !double.IsFinite(unscaledDeltaSeconds) || unscaledDeltaSeconds <= 0) return Displayed;
            double step = (target - Displayed) * (1.0 - Math.Exp(-Responsiveness * unscaledDeltaSeconds));
            Displayed = target - Displayed < 0.001 ? target : Math.Min(target, Displayed + step);
            return Displayed;
        }

        public void Reset() => Displayed = 0;
    }

    /// <summary>
    /// A világgenerálás alapértelmezett szakaszai (WF-LOAD-001). A súlyok
    /// kiinduló becslések; a Core-kötés a <c>PlanetGridMesh.Build</c> mért
    /// idejével pontosítja vagy saját listát ad.
    /// </summary>
    public static class WorldGenerationStages
    {
        public const string Crust = "crust";
        public const string Plates = "plates";
        public const string Climate = "climate";
        public const string Oceans = "oceans";
        public const string Renderer = "renderer";

        public static IReadOnlyList<LoadingStage> CreateDefault() => new[]
        {
            new LoadingStage(Crust, "loading.crust", 2),
            new LoadingStage(Plates, "loading.plates", 3),
            new LoadingStage(Climate, "loading.climate", 2),
            new LoadingStage(Oceans, "loading.oceans", 1),
            new LoadingStage(Renderer, "loading.renderer", 2, isCancellable: false),
        };
    }
}
