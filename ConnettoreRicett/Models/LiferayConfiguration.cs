namespace ConnettoreRicett.Models
{
    public class LiferayConfiguration
    {
        public VocabularySettings Vocabularies { get; set; } = new();
        public DefaultTaxonomySettings DefaultTaxonomies { get; set; } = new();
        public ContentStructureSettings ContentStructure { get; set; } = new();
        public FolderSettings Folders { get; set; } = new();
        public DefaultImageSettings DefaultImage { get; set; } = new();
    }

    public class VocabularySettings
    {
        public int Licenza { get; set; }
        public int CategoriaRicetta { get; set; }
        public int RiferimentoGeografico { get; set; }
        public int Tema { get; set; }
    }

    public class DefaultTaxonomySettings
    {
        public int LicenzaId { get; set; }
        public int[] TemaIds { get; set; } = Array.Empty<int>();
    }

    public class ContentStructureSettings
    {
        public int Id { get; set; }
    }

    public class FolderSettings
    {
        public int Documents { get; set; }
        public int StructuredContent { get; set; }
    }

    public class DefaultImageSettings
    {
        public int Id { get; set; }
        public string Url { get; set; } = string.Empty;
    }
}