using System;

public interface ITemporalEntity
{
	DateTime FromSysDate { get; set; }
	DateTime ToSysDate { get; set; }
}
