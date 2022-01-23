using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

public class ProductType
{
    [Key]
    [Column(TypeName = "int")]
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public int Id { get; set; }
    [Column(TypeName = "varchar(200)")]
    public string Name { get; set; }
    [ForeignKey("Id")]
    public virtual ProductClass ProductClass { get; set; }
    public virtual List<Product> Products{ get; set; }
}