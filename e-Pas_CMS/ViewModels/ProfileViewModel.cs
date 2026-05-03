namespace e_Pas_CMS.ViewModels
{
    public class ProfileViewModel
    {
        public string Id { get; set; }
        public string Username { get; set; }
        public string Name { get; set; }
        public string PhoneNumber { get; set; }
        public string Email { get; set; }
    }

    public class ChangePasswordViewModel
    {
        public string CurrentPassword { get; set; }
        public string NewPassword { get; set; }
        public string ConfirmPassword { get; set; }
    }
}
