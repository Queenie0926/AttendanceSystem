using Attendance_System.Models;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Xml.Linq;

namespace Attendance_System.Views
{
    public partial class EnrollmentView : UserControl
    {
        private readonly ObservableCollection<StaffRecord> _staff = new()
        {
            new StaffRecord { Id = 1, Name = "Juan Dela Cruz", Position = "IT Staff", RfidUid = "A1B2C3D4" },
            new StaffRecord { Id = 2, Name = "Maria Santos", Position = "HR Staff", RfidUid = "E5F6G7H8" }
        };

        public EnrollmentView()
        {
            InitializeComponent();
            StaffGrid.ItemsSource = _staff;
        }

        private void EnrollButton_Click(object sender, RoutedEventArgs e)
        {
            string name = TxtName.Text.Trim();
            string position = TxtPosition.Text.Trim();
            string uid = TxtRfidUid.Text.Trim();
            string pin = TxtPin.Password.Trim();

            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(uid) || string.IsNullOrEmpty(pin))
            {
                StatusText.Text = "Name, RFID UID, and PIN are required.";
                return;
            }

            _staff.Add(new StaffRecord
            {
                Id = _staff.Count + 1,
                Name = name,
                Position = position,
                RfidUid = uid
            });

            StatusText.Text = $"{name} enrolled successfully.";
            TxtName.Clear();
            TxtPosition.Clear();
            TxtRfidUid.Clear();
            TxtPin.Clear();
        }
    }
}