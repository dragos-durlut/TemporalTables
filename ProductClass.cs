using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

public class ProductClass
{
    [Key]
    [Column(TypeName = "int")]
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public int Id { get; set; }

    [Column(TypeName = "varchar(200)")]
    public string Name { get; set; }
    
    public virtual List<ProductType> ProductTypes { get; set; }
}