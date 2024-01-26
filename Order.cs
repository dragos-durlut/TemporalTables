using System;

public class Order : ITemporalEntity
{
    public Guid Id { get; set; }
    public DateTime OrderDate { get; set; }
    
    public Product Product { get; set; }
    public Customer Customer { get; set; }

    public DateTime FromSysDate { get; set; }

    public DateTime ToSysDate {  get; set; }
}