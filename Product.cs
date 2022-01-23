using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

public class Product
{
    public Guid Id { get; set; }
    public string Name { get; set; }
    public decimal Price { get; set; }
    
    [Required]
    [ForeignKey("Id")]
    public virtual ProductType ProductType { get; set; }

    public virtual List<Order> Orders { get; set; }
}