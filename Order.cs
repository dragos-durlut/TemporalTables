using System;

public class Order
{
    public Guid Id { get; set; }
    public DateTime OrderDate { get; set; }
    
    public Product Product { get; set; }
    public Customer Customer { get; set; }    
}