using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Attendance_System.Models
{
    /// <summary>Editable row on the Users screen; tracks unsaved changes.</summary>
    public class UserProfileRecord : INotifyPropertyChanged
    {
        public Guid Id { get; init; }
        public string Email { get; init; } = "";
        public bool IsCurrentUser { get; init; }

        private string _savedFullName = "";
        private string _savedRole = "";

        private string _fullName = "";
        public string FullName
        {
            get => _fullName;
            set { _fullName = value; OnChanged(); OnChanged(nameof(IsDirty)); }
        }

        private string _role = "";
        public string Role
        {
            get => _role;
            set { _role = value; OnChanged(); OnChanged(nameof(IsDirty)); }
        }

        // Admins can't change their own role here, so they can't lock themselves out.
        public bool CanEditRole => !IsCurrentUser;

        public bool IsDirty => FullName != _savedFullName || Role != _savedRole;

        public void MarkSaved()
        {
            _savedFullName = FullName;
            _savedRole = Role;
            OnChanged(nameof(IsDirty));
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
