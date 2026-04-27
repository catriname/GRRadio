namespace GRRadio.Services;

/// <summary>
/// Simplified SGP4 satellite propagator — C# port of the public domain algorithm.
/// Suitable for amateur satellite pass prediction (LEO, ~200–2000 km altitude).
/// </summary>
public static class Sgp4
{
    private const double TwoPi = 2 * Math.PI;
    private const double Deg2Rad = Math.PI / 180;
    private const double MinutesPerDay = 1440.0;
    private const double Xke = 0.0743669161; // sqrt(GM) in er^3/2/min
    private const double Ae = 1.0;           // earth radii
    private const double Re = 6378.137;      // km
    private const double Vk = 60.0 / Re;    // km/s to er/min

    public record EciPosition(double X, double Y, double Z, double Vx, double Vy, double Vz);

    public static EciPosition? Propagate(TleParsed tle, DateTime utcTime)
    {
        double tsince = (utcTime - tle.Epoch).TotalMinutes;
        return RunSgp4(tle, tsince);
    }

    private static EciPosition? RunSgp4(TleParsed t, double tsince)
    {
        try
        {
            // Recover original mean motion and semi-major axis
            double a1 = Math.Pow(Xke / t.N0, 2.0 / 3);
            double cosio = Math.Cos(t.Inclo);
            double theta2 = cosio * cosio;
            double x3thm1 = 3 * theta2 - 1;
            double eosq = t.Ecco * t.Ecco;
            double betao2 = 1 - eosq;
            double betao = Math.Sqrt(betao2);
            double del1 = 1.5 * 0.00108263 * x3thm1 / (a1 * a1 * betao * betao2);
            double ao = a1 * (1 - del1 * (0.5 + del1 * (1 + 134.0 / 81 * del1)));
            double delo = 1.5 * 0.00108263 * x3thm1 / (ao * ao * betao * betao2);
            double xnodp = t.N0 / (1 + delo);
            double aodp = ao / (1 - delo);

            // Initialization
            double isimp = 0;
            double s4 = 1.012229;
            double qoms24 = 1.880279e-09;
            double perige = (aodp * (1 - t.Ecco) - Ae) * Re;

            if (perige < 220)
            {
                isimp = 1;
                s4 = 1.0 + 78.0 / Re;
                qoms24 = Math.Pow(((120 - s4) / Re) + Ae, 4);
            }

            double pinvsq = 1 / (aodp * aodp * betao2 * betao2);
            double tsi = 1 / (aodp - s4);
            double eta = aodp * t.Ecco * tsi;
            double etasq = eta * eta;
            double eeta = t.Ecco * eta;
            double psisq = Math.Abs(1 - etasq);
            double coef = qoms24 * Math.Pow(tsi, 4);
            double coef1 = coef / Math.Pow(psisq, 3.5);

            double c2 = coef1 * xnodp * (aodp * (1 + 1.5 * etasq + eeta * (4 + etasq)) +
                0.75 * 0.00108263 * tsi / psisq * x3thm1 * (8 + 3 * etasq * (8 + etasq)));
            double c1 = t.Bstar * c2;
            double sinio = Math.Sin(t.Inclo);
            double a3ovk2 = -0.00108263 / Ae * 1.5;
            double c3 = coef * tsi * a3ovk2 * xnodp * sinio / t.Ecco;
            double x1mth2 = 1 - theta2;
            double c4 = 2 * xnodp * coef1 * aodp * betao2 *
                (eta * (2 + 0.5 * etasq) + t.Ecco * (0.5 + 2 * etasq) -
                2 * 0.00108263 * tsi / (aodp * psisq) *
                (-3 * x3thm1 * (1 - 2 * eeta + etasq * (1.5 - 0.5 * eeta)) +
                0.75 * x1mth2 * (2 * etasq - eeta * (1 + etasq)) * Math.Cos(2 * t.Argpo)));

            double c5, c6, sinmo, d2, d3, d4, t3cof, t4cof, t5cof;
            c5 = c6 = sinmo = d2 = d3 = d4 = t3cof = t4cof = t5cof = 0;

            if (isimp == 0)
            {
                double c1sq = c1 * c1;
                d2 = 4 * aodp * tsi * c1sq;
                double temp = d2 * tsi * c1 / 3;
                d3 = (17 * aodp + s4) * temp;
                d4 = 0.5 * temp * aodp * tsi * (221 * aodp + 31 * s4) * c1;
                t3cof = d2 + 2 * c1sq;
                t4cof = 0.25 * (3 * d3 + c1 * (12 * d2 + 10 * c1sq));
                t5cof = 0.2 * (3 * d4 + 12 * c1 * d3 + 6 * d2 * d2 + 15 * c1sq * (2 * d2 + c1sq));
                sinmo = Math.Sin(t.Mo);
                c5 = 2 * coef1 * aodp * betao2 * (1 + 2.75 * (etasq + eeta) + eeta * etasq);
                c6 = coef * tsi * a3ovk2 * xnodp * sinio * t.Ecco;
            }

            // Secular effects of drag and gravitation
            double xmdf = t.Mo + (1 + 1.5 * 0.00108263 * x3thm1 / (aodp * aodp * betao2 * betao2) *
                (1 - 1.5 * eosq) + (coef1 * 0.00108263) * (-x3thm1 + eosq + 3 * theta2 - eosq * theta2)) * xnodp * tsince;

            double argpdf = t.Argpo + (-0.5 * 0.00108263 * (3 - 5 * theta2) / (aodp * betao2) +
                0.0625 * 0.00108263 * 0.00108263 * (7 - 114 * theta2 + 395 * theta2 * theta2) / (aodp * aodp * betao2 * betao2)) * xnodp * tsince;

            double xnoddf = t.NodeO + (xnodp * (-1.5 * 0.00108263 * cosio / (aodp * aodp * betao2)) * tsince);

            double xmp, ep, xincp, xnodp2;
            if (isimp == 0)
            {
                double delomg = 0; // simplified - skip long-period periodics for brevity
                double delm = (1 + eta * Math.Cos(t.Mo)) * (1 + eta * Math.Cos(t.Mo)) * (1 + eta * Math.Cos(t.Mo));
                double delta = c1 * tsince;
                xmp = xmdf + delomg + c5 * (Math.Sin(t.Mo + argpdf) - sinmo);
                ep = t.Ecco - t.Bstar * c4 * tsince - t.Bstar * c5 * (Math.Sin(xmp) - sinmo);
                xincp = t.Inclo;
                xnodp2 = xnoddf;
                argpdf -= 5 * tsince * tsince * delta;
            }
            else
            {
                xmp = xmdf;
                ep = t.Ecco - t.Bstar * c4 * tsince;
                xincp = t.Inclo;
                xnodp2 = xnoddf;
            }

            xnodp2 = xnodp2 % TwoPi;
            if (xnodp2 < 0) xnodp2 += TwoPi;

            // Solve Kepler's equation
            double xlt = xmp + argpdf;
            double e = ep;
            if (e < 1e-6) e = 1e-6;
            double em = xmp % TwoPi;
            double ec = e;

            double u = em;
            for (int i = 0; i < 10; i++)
            {
                double du = (u - e * Math.Sin(u) - em) / (1 - e * Math.Cos(u));
                u -= du;
                if (Math.Abs(du) < 1e-12) break;
            }

            double sinEk = Math.Sin(u);
            double cosEk = Math.Cos(u);
            double ek = Math.Atan2(Math.Sqrt(1 - e * e) * sinEk, cosEk - e);

            // Short-period periodics
            double el2 = ek + argpdf;
            double r = aodp * (1 - e * cosEk);
            double rdot = Xke * Math.Sqrt(aodp) * e * sinEk / r;
            double rfdot = Xke * Math.Sqrt(aodp * (1 - e * e)) / r;
            double cosu = Math.Cos(el2);
            double sinu = Math.Sin(el2);

            // ECI position and velocity (Earth radii and er/min)
            double cosnode = Math.Cos(xnodp2);
            double sinnode = Math.Sin(xnodp2);
            double cosi = Math.Cos(xincp);
            double sini = Math.Sin(xincp);

            double xmx = -sinnode * cosi;
            double xmy = cosnode * cosi;
            double ux = xmx * sinu + cosnode * cosu;
            double uy = xmy * sinu + sinnode * cosu;
            double uz = sini * sinu;

            double x = r * ux * Re;
            double y = r * uy * Re;
            double z = r * uz * Re;

            double vx = (rdot * ux + rfdot * (-xmx * cosu + cosnode * sinu)) / Vk;
            double vy = (rdot * uy + rfdot * (-xmy * cosu + sinnode * sinu)) / Vk;
            double vz = (rdot * uz + rfdot * sini * cosu) / Vk;

            return new EciPosition(x, y, z, vx, vy, vz);
        }
        catch { return null; }
    }

    // ── ECI → Observer look angles ────────────────────────────────────────────

    public static (double Azimuth, double Elevation, double Range) GetLookAngles(
        EciPosition sat, double latDeg, double lonDeg, double altKm, DateTime utcTime)
    {
        double lst = LocalSiderealTime(utcTime, lonDeg);
        double lat = latDeg * Deg2Rad;

        double cosLat = Math.Cos(lat);
        double sinLat = Math.Sin(lat);
        double cosLst = Math.Cos(lst);
        double sinLst = Math.Sin(lst);

        // Observer position in ECI
        double rObs = Re + altKm;
        double ox = rObs * cosLat * cosLst;
        double oy = rObs * cosLat * sinLst;
        double oz = rObs * sinLat;

        // Range vector
        double rx = sat.X - ox;
        double ry = sat.Y - oy;
        double rz = sat.Z - oz;
        double range = Math.Sqrt(rx * rx + ry * ry + rz * rz);

        // SEZ (South-East-Z) topocentric
        double south = sinLat * cosLst * rx + sinLat * sinLst * ry - cosLat * rz;
        double east  = -sinLst * rx + cosLst * ry;
        double zenit = cosLat * cosLst * rx + cosLat * sinLst * ry + sinLat * rz;

        double el = Math.Asin(zenit / range) / Deg2Rad;
        double az = Math.Atan2(-east, south) / Deg2Rad;
        if (az < 0) az += 360;

        return (az, el, range);
    }

    private static double LocalSiderealTime(DateTime utc, double lonDeg)
    {
        double jd = ToJulianDate(utc);
        double t = (jd - 2451545.0) / 36525.0;
        double gmst = 280.46061837 + 360.98564736629 * (jd - 2451545.0) +
                      0.000387933 * t * t - t * t * t / 38710000;
        return ((gmst + lonDeg) % 360 + 360) % 360 * Deg2Rad;
    }

    public static double ToJulianDate(DateTime utc)
    {
        int y = utc.Year, m = utc.Month, d = utc.Day;
        double h = utc.Hour + utc.Minute / 60.0 + utc.Second / 3600.0;
        if (m <= 2) { y--; m += 12; }
        int a = y / 100;
        int b = 2 - a + a / 4;
        return (int)(365.25 * (y + 4716)) + (int)(30.6001 * (m + 1)) + d + h / 24 + b - 1524.5;
    }
}

/// <summary>Parsed TLE orbital elements.</summary>
public class TleParsed
{
    public string Name    { get; set; } = string.Empty;
    public int    NoradId { get; set; }
    public DateTime Epoch { get; set; }
    public double N0 { get; set; }    // mean motion (rad/min)
    public double Ecco { get; set; }  // eccentricity
    public double Inclo { get; set; } // inclination (rad)
    public double NodeO { get; set; } // RAAN (rad)
    public double Argpo { get; set; } // argument of perigee (rad)
    public double Mo { get; set; }    // mean anomaly (rad)
    public double Bstar { get; set; } // drag term

    private const double TwoPi = 2 * Math.PI;
    private const double Deg2Rad = Math.PI / 180;
    private const double Xke = 0.0743669161;

    public static TleParsed? Parse(string name, string line1, string line2)
    {
        try
        {
            var t = new TleParsed
            {
                Name    = name.Trim(),
                NoradId = int.Parse(line1[2..7].Trim())
            };

            // Epoch
            double epochYear = double.Parse(line1[18..20]);
            double epochDay  = double.Parse(line1[20..32]);
            int fullYear = (int)(epochYear < 57 ? 2000 + epochYear : 1900 + epochYear);
            t.Epoch = new DateTime(fullYear, 1, 1, 0, 0, 0, DateTimeKind.Utc)
                          .AddDays(epochDay - 1);

            // Bstar
            string bstarStr = line1[53..61].Trim();
            if (bstarStr.Length >= 6)
            {
                if (double.TryParse(bstarStr[..5], out var bm) &&
                    int.TryParse(bstarStr[5..], out var be))
                    t.Bstar = bm * 1e-5 * Math.Pow(10, be);
            }

            // Line 2 elements
            t.Inclo = double.Parse(line2[8..16].Trim()) * Deg2Rad;
            t.NodeO = double.Parse(line2[17..25].Trim()) * Deg2Rad;
            t.Ecco  = double.Parse("0." + line2[26..33].Trim());
            t.Argpo = double.Parse(line2[34..42].Trim()) * Deg2Rad;
            t.Mo    = double.Parse(line2[43..51].Trim()) * Deg2Rad;
            double n0Revs = double.Parse(line2[52..63].Trim());
            t.N0 = n0Revs * TwoPi / 1440.0; // rev/day → rad/min

            return t;
        }
        catch { return null; }
    }
}
