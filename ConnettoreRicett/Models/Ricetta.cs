using ConnettoreRicett.Controllers;
using System.ComponentModel.DataAnnotations;

namespace ConnettoreRicett.Models
{
    public class Ricetta
    {
        [Required(ErrorMessage = "Il campo Denominazione è obbligatorio.")]
        public string Denominazione { get; set; }

        public string Presentazione { get; set; }
        public string Difficolta { get; set; }
        public string Preparazione { get; set; }
        public string Cottura { get; set; }
        public string Dosi { get; set; }
        public string Costo { get; set; }
        public string Ingredienti { get; set; }

        [Required(ErrorMessage = "Il campo Latitudine è obbligatorio.")]
        [RegularExpression(@"^\d+(\.\d{1,99})?$", ErrorMessage = "La Latitudine deve essere un numero decimale.")]
        public string Latitudine { get; set; }

        [Required(ErrorMessage = "Il campo Longitudine è obbligatorio.")]
        [RegularExpression(@"^\d+(\.\d{1,99})?$", ErrorMessage = "La Longitudine deve essere un numero decimale.")]
        public string Longitudine { get; set; }
        public string AreaDiInteresse { get; set; }
        public string RiferimentoGeograficoString { get; set; }
        public List<Meta> MetaFields { get; set; } = new List<Meta>();

        public string ImmaginePrincipale { get; set; }

        public int ImmaginePrincipaleId { get; set; }

        public List<string> AltraImmagini { get; set; } = new List<string>();
        public List<int> AltraImmaginiId { get; set; } = new List<int>();
        //public string AltraImmagine { get; set; } /*= "/documents/d/guest/astronaut-png";
        //public int AltraImmagineId { get; set; } /*= 31926;*/
        //public string DestinazioneWebContent { get; set; } = "Servizio Pubblico Test";
        //public int DestinazioneWebContentId { get; set; } = 32606;

        [Required(ErrorMessage = "Seleziona almeno una categoria per ogni gruppo")]
        public List<int> TaxonomyCategoryIds { get; set; } = new List<int>();
        public Dictionary<string, List<TaxonomyCategory>> VocabularyCategories { get; set; } =
            new Dictionary<string, List<TaxonomyCategory>>();
    }



    public class TaxonomyVocabulary
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public List<TaxonomyCategory> Categories { get; set; }
    }

    public class TaxonomyCategory
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public int? ParentCategoryId { get; set; }
        public List<TaxonomyCategory> SubCategories { get; set; } = new List<TaxonomyCategory>();
    }

    public class TaxonomyVocabularyResponse
    {
        public List<TaxonomyVocabulary> Items { get; set; }
        public int TotalCount { get; set; }
    }

    public class TaxonomyCategoryResponse
    {
        public List<TaxonomyCategory> Items { get; set; }
        public int TotalCount { get; set; }
    }

    public class Meta
    {
        public string Chiave { get; set; }
        public string Valore { get; set; }
    }

    public class DocumentResponseList<T>
    {
        public int TotalCount { get; set; }
        public List<T> Items { get; set; }
    }

    public class DocumentResponseInternal
    {
        public int Id { get; set; }
        public string ContentUrl { get; set; }
        public long SizeInBytes { get; set; }
    }

    public class DocumentResponse
    {
        public int Id { get; set; }
        public string ContentUrl { get; set; }
    }
}
