namespace SysSuite.Models
{
    /// <summary>Riga lista Analisi Disco (binding Size + Path).</summary>
    public class DiscoItemRow
    {
        public string Size { get; set; } = "";
        public string Path { get; set; } = "";
        public long SizeBytes { get; set; }
    }
}
