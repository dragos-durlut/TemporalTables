using System;
using System.ComponentModel.DataAnnotations.Schema;

public class Order : ITemporalEntity
{
    public Guid Id { get; set; }
    public DateTime OrderDate { get; set; }
    
    public Product Product { get; set; }
    public Customer Customer { get; set; }

    [NotMapped]
    public DateTime FromSysDate { get; set; }
    [NotMapped]
    public DateTime ToSysDate {  get; set; }
}