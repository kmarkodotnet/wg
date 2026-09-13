#nullable enable
using System;
using System.Collections.Concurrent;
using UnityEngine;
using WorldGen.App.Diagnostics;

namespace WorldGen.App.UnityBinding
{
    /// <summary>
    /// A Unity naplóüzeneteit az <see cref="AppLogger"/>-be vezeti (WF-DIAG-001).
    /// A callback bármely szálról jöhet; az <see cref="AppLogger"/> szálbiztos. Az
    /// ismétlődő kivételeket ritkítja, az első előfordulásukat a főszál felé sorba teszi,
    /// hogy a felhasználó egyszer kapjon értesítést.
    /// </summary>
    public sealed class UnityLogBridge : IDisposable
    {
        private readonly AppLogger _logger;
        private readonly ExceptionThrottle _throttle;
        private readonly ConcurrentQueue<string> _firstExceptions = new ConcurrentQueue<string>();
        private bool _attached;

        public UnityLogBridge(AppLogger logger, ExceptionThrottle throttle)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _throttle = throttle ?? throw new ArgumentNullException(nameof(throttle));
        }

        public void Attach()
        {
            if (_attached) return;
            Application.logMessageReceivedThreaded += OnLogMessage;
            _attached = true;
        }

        /// <summary>Főszálról hívandó: egy még nem jelzett, új kivétel szövege.</summary>
        public bool TryTakeFirstException(out string text) => _firstExceptions.TryDequeue(out text!);

        public void Dispose()
        {
            if (!_attached) return;
            Application.logMessageReceivedThreaded -= OnLogMessage;
            _attached = false;
        }

        private void OnLogMessage(string condition, string stackTrace, LogType type)
        {
            switch (type)
            {
                case LogType.Log:
                    _logger.Log(LogLevel.Info, "Unity", condition);
                    break;
                case LogType.Warning:
                    _logger.Log(LogLevel.Warning, "Unity", condition);
                    break;
                case LogType.Error:
                case LogType.Assert:
                    _logger.Log(LogLevel.Error, "Unity", WithStack(condition, stackTrace));
                    break;
                case LogType.Exception:
                    var occurrence = _throttle.Register(condition, stackTrace);
                    if (occurrence.ShouldLog) _logger.Log(LogLevel.Error, "Unity", WithStack(condition, stackTrace));
                    if (occurrence.IsFirst) _firstExceptions.Enqueue(WithStack(condition, stackTrace));
                    break;
            }
        }

        private static string WithStack(string condition, string stackTrace)
            => string.IsNullOrEmpty(stackTrace) ? condition : condition + "\n" + stackTrace.TrimEnd();
    }
}
