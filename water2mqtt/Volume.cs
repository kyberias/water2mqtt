using System.Globalization;

namespace water2mqtt;

public class Volume
{
    protected bool Equals(Volume other)
    {
        return liters == other.liters;
    }

    public override bool Equals(object? obj)
    {
        if (obj is null) return false;
        if (ReferenceEquals(this, obj)) return true;
        if (obj.GetType() != GetType()) return false;
        return Equals((Volume)obj);
    }

    public override int GetHashCode()
    {
        return liters.GetHashCode();
    }

    private readonly decimal liters;

    private Volume(decimal liters)
    {
        this.liters = liters;
    }

    public decimal ToCubicMeters() => liters / 1000;

    public decimal ToLiters() => liters;

    public static Volume FromLiters(decimal liters) => new(liters);
    public static Volume FromCubicMeters(decimal cubes) => new(cubes * 1000);

    public static Volume Zero = new(0);

    public static Volume operator +(Volume? a, Volume? b)
    {
        if (a is null)
        {
            a = Zero;
        }

        if (b is null)
        {
            b = Zero;
        }

        return FromLiters(a.ToLiters() + b.ToLiters());
    }

    public static Volume operator -(Volume a)
    {
        return FromLiters(-a.liters);
    }

    public static Volume operator -(Volume a, Volume b)
    {
        return FromLiters(a.liters - b.liters);
    }

    public static FlowRate operator /(Volume a, TimeSpan t)
    {
        return new FlowRate(a, t);
    }

    public static bool operator !=(Volume? a, Volume? b)
    {
        if (a is null && b is null)
        {
            return false;
        }

        if (a is null || b is null)
        {
            return true;
        }

        return a.liters != b.liters;
    }

    public static bool operator ==(Volume? a, Volume? b)
    {
        if (a is null && b is null)
        {
            return true;
        }

        if (a is null || b is null)
        {
            return false;
        }

        return a.liters == b.liters;
    }

    public static bool operator <(Volume a, Volume b)
    {
        return a.liters < b.liters;
    }

    public static bool operator >(Volume a, Volume b)
    {
        return a.liters > b.liters;
    }

    public override string ToString()
    {
        return ToCubicMeters().ToString(CultureInfo.CurrentCulture);
    }
}