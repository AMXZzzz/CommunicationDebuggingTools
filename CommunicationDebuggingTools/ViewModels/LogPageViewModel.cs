using System;
using System.Collections.ObjectModel;
using System.Windows.Input;
using CommunicationDebuggingTools.Core.Logging;

namespace CommunicationDebuggingTools.ViewModels {

    /// <summary>
    /// 通讯日志页 ViewModel。
    /// 订阅 <see cref="IAppLogger.EntryAdded"/>，由 Page 在 UI 线程调用 <see cref="AppendEntry"/>。
    /// </summary>
    public sealed class LogPageViewModel : ViewModelBase {

        private readonly IAppLogger _logger;

        /// <summary>绑定到 ListBox 的条目（仅 UI 线程读写）。</summary>
        public ObservableCollection<LogEntry> Entries { get; } =
            new ObservableCollection<LogEntry>();

        public ICommand ClearCommand { get; }

        /// <summary>后台线程新日志；Page 负责 Dispatcher 后再 AppendEntry。</summary>
        public event Action<LogEntry> EntryAdded;

        public LogPageViewModel (IAppLogger logger) {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            ClearCommand = new RelayCommand(Clear);

            foreach (LogEntry e in _logger.GetRecent())
                Entries.Add(e);

            _logger.EntryAdded += OnLoggerEntryAdded;
        }

        private void OnLoggerEntryAdded (LogEntry entry) {
            EntryAdded?.Invoke(entry);
        }

        /// <summary>必须在 UI 线程调用。</summary>
        public void AppendEntry (LogEntry entry) {
            if (entry == null) return;
            Entries.Add(entry);
            while (Entries.Count > 500)
                Entries.RemoveAt(0);
        }

        public void Clear () {
            _logger.Clear();
            Entries.Clear();
        }

        /// <summary>Page Unloaded 时退订，避免泄漏。</summary>
        public void Detach () {
            _logger.EntryAdded -= OnLoggerEntryAdded;
        }
    }
}