using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;

public class Customer : ITemporalEntity
{
    public Guid Id { get; set; }
    public string Name  { get; set; }

    public virtual List<Order> Orders { get; set; }

    [NotMapped]
    public DateTime FromSysDate { get; set; }
    [NotMapped]
    public DateTime ToSysDate { get; set; }
}