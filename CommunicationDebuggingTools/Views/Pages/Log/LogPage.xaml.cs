using System;
using System.Windows;
using System.Windows.Controls;
using CommunicationDebuggingTools.Core.Logging;
using CommunicationDebuggingTools.ViewModels;

namespace CommunicationDebuggingTools.Views.Pages.Log {

    public partial class LogPage : Page {

        private readonly LogPageViewModel _vm;

        public LogPage (LogPageViewModel viewModel) {
            _vm = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
            InitializeComponent();
            DataContext = _vm;

            listLog.ItemsSource = _vm.Entries;

            _vm.EntryAdded += OnEntryAdded;
            Unloaded += (_, __) => {
                _vm.EntryAdded -= OnEntryAdded;
                _vm.Detach();
            };
        }

        private void OnEntryAdded (LogEntry entry) {
            Dispatcher.BeginInvoke(new Action(() => {
                _vm.AppendEntry(entry);
                if (_vm.Entries.Count > 0)
                    listLog.ScrollIntoView(_vm.Entries[_vm.Entries.Count - 1]);
            }));
        }

        /// <summary>对应 XAML：Click="BtnClear_Click"</summary>
        private void BtnClear_Click (object sender, RoutedEventArgs e) {
            if (_vm.ClearCommand != null && _vm.ClearCommand.CanExecute(null))
                _vm.ClearCommand.Execute(null);
        }
    }
}