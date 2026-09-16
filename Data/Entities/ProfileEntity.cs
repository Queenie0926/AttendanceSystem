using Postgrest.Attributes;
using Postgrest.Models;

namespace Attendance_System.Data.Entities
{
    // One row per auth user (profiles.id → auth.users.id), created by the
    // on_auth_user_created trigger. `role` is the user_role enum, sent as text.
    [Table("profiles")]
    public class ProfileEntity : BaseModel
    {
        [PrimaryKey("id", false)]
        public Guid Id { get; set; }

        [Column("email")]
        public string? Email { get; set; }

        [Column("full_name")]
        public string? FullName { get; set; }

        [Column("role")]
        public string Role { get; set; } = UserRoles.Viewer;

        [Column("created_at")]
        public DateTimeOffset CreatedAt { get; set; }
    }

    /// <summary>Values of the user_role enum in Supabase.</summary>
    public static class UserRoles
    {
        public const string Admin = "admin";
        public const string Hr = "hr";
        public const string Viewer = "viewer";

        public static readonly string[] All = { Admin, Hr, Viewer };
    }
}
