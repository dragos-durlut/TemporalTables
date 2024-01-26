using System;
using System.Collections.Generic;

public class Customer : ITemporalEntity
{
    public Guid Id { get; set; }
    public string Name  { get; set; }

    public virtual List<Order> Orders { get; set; }
        
    public DateTime FromSysDate { get; set; }
    
    public DateTime ToSysDate { get; set; }
}