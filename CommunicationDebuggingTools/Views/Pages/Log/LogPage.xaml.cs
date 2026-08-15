using System;
using System.Windows;
using System.Windows.Controls;
using CommunicationDebuggingTools.Core.Logging;
using CommunicationDebuggingTools.ViewModels;

namespace CommunicationDebuggingTools.Views.Pages.Log {

    /// <summary>
    /// 通讯日志页 code-behind：绑定 VM，将后台 EntryAdded 事件调度到 UI 线程。
    /// </summary>
    public partial class LogPage : Page {

        private readonly LogPageViewModel _vm;

        public LogPage (LogPageViewModel viewModel) {
            _vm = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
            InitializeComponent();
            DataContext = _vm;

            listLog.ItemsSource = _vm.Entries;

            // EntryAdded 从后台线程触发，必须 Dispatcher 调度
            _vm.EntryAdded += OnEntryAdded;
            Unloaded += (_, __) => _vm.EntryAdded -= OnEntryAdded;
        }

        private void OnEntryAdded (LogEntry entry) {
            Dispatcher.BeginInvoke(new Action(() => {
                _vm.AppendEntry(entry);
                if (_vm.Entries.Count > 0)
                    listLog.ScrollIntoView(_vm.Entries[_vm.Entries.Count - 1]);
            }));
        }
    }
}