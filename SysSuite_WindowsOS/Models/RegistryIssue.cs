namespace SysSuite.Models
{
    public class RegistryIssue
    {
        public string Category    { get; set; } = "";  // MissingDll, InvalidPath, ObsoleteKey
        public string KeyPath     { get; set; } = "";
        public string ValueName   { get; set; } = "";
        public string Description { get; set; } = "";
    }
}
