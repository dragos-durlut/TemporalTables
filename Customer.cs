using System;
using System.Collections.Generic;

public class Customer
{
    public Guid Id { get; set; }
    public string Name  { get; set; }

    public virtual List<Order> Orders { get; set; }
}