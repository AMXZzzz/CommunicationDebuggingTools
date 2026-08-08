using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace CommunicationDebuggingTools.Views.Pages.MESMonitor.Controls {
    public partial class MesAlarmDetail : UserControl {
        public event Action CloseRequested;
        public event Action BackRequested;

        public MesAlarmDetail () {
            InitializeComponent();
        }

        private void BtnX_Click (object sender, MouseButtonEventArgs e) {
            CloseRequested?.Invoke();
        }

        private void BtnBack_Click (object sender, RoutedEventArgs e) {
            BackRequested?.Invoke();
        }
    }
}