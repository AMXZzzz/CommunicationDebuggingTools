using System;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using CommunicationDebuggingTools.Core.Logging;
using CommunicationDebuggingTools.Services;

namespace CommunicationDebuggingTools.Views.Pages.Log {
    /// <summary>
    /// 通讯日志页：订阅 <see cref="MyAppServices.Logger"/>，展示环形缓冲。
    /// </summary>
    public partial class LogPage : Page {
        private readonly ObservableCollection<LogEntry> _items =
            new ObservableCollection<LogEntry>();

        public LogPage () {
            InitializeComponent();
            listLog.ItemsSource = _items;
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        private void OnLoaded (object sender, RoutedEventArgs e) {
            _items.Clear();

            if (MyAppServices.Logger == null)
                return;

            foreach (LogEntry entry in MyAppServices.Logger.GetRecent())
                _items.Add(entry);

            MyAppServices.Logger.EntryAdded += OnEntryAdded;
        }

        private void OnUnloaded (object sender, RoutedEventArgs e) {
            if (MyAppServices.Logger != null)
                MyAppServices.Logger.EntryAdded -= OnEntryAdded;
        }

        private void OnEntryAdded (LogEntry entry) {
            if (entry == null)
                return;

            Dispatcher.BeginInvoke(new Action(() => {
                _items.Add(entry);
                if (_items.Count > 0)
                    listLog.ScrollIntoView(_items[_items.Count - 1]);
            }));
        }

        private void BtnClear_Click (object sender, RoutedEventArgs e) {
            if (MyAppServices.Logger != null)
                MyAppServices.Logger.Clear();
            _items.Clear();
        }
    }
}