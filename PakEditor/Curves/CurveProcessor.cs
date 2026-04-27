using System;
using System.Collections.Generic;
using CUE4Parse.UE4.Objects.Engine.Curves;

namespace PakEditor.Curves;

internal static class CurveProcessor
{
    private const int PointsPerSegment = 10;

    public static (double[] Times, double[] Values) ProcessCurve(
        IReadOnlyList<FRichCurveKey> keys,
        double conversionFactor = 1.0)
    {
        if (keys == null || keys.Count == 0)
            return (Array.Empty<double>(), Array.Empty<double>());

        var parsed = new List<FRichCurveKey>(keys);
        parsed.Sort((a, b) => a.Time.CompareTo(b.Time));

        var timePts  = new List<double>();
        var valuePts = new List<double>();

        if (parsed[0].Time > 0.0)
        {
            timePts.Add(0.0);
            valuePts.Add(parsed[0].Value * conversionFactor);
        }

        for (int i = 0; i < parsed.Count; i++)
        {
            var k = parsed[i];
            timePts.Add(k.Time);
            valuePts.Add(k.Value * conversionFactor);

            if (i < parsed.Count - 1)
            {
                var    k2 = parsed[i + 1];
                double dt = k2.Time - k.Time;
                if (dt <= 0.0) continue;

                if (k.InterpMode == ERichCurveInterpMode.RCIM_Cubic)
                    InterpolateCubicSegment(k, k2, dt, conversionFactor, timePts, valuePts);
                else if (k.InterpMode != ERichCurveInterpMode.RCIM_Constant)
                    InterpolateLinearSegment(k, k2, dt, conversionFactor, timePts, valuePts);
            }
        }

        return (timePts.ToArray(), valuePts.ToArray());
    }

    private static void InterpolateLinearSegment(
        FRichCurveKey k, FRichCurveKey k2, double dt, double cf,
        List<double> timePts, List<double> valuePts)
    {
        double p1 = k.Value  * cf;
        double p2 = k2.Value * cf;
        for (int step = 1; step < PointsPerSegment; step++)
        {
            double s = step / (double)PointsPerSegment;
            timePts.Add(k.Time + s * dt);
            valuePts.Add(p1 + s * (p2 - p1));
        }
    }

    private static void InterpolateCubicSegment(
        FRichCurveKey k, FRichCurveKey k2, double dt, double cf,
        List<double> timePts, List<double> valuePts)
    {
        bool leaveWeighted =
            k.TangentWeightMode  == ERichCurveTangentWeightMode.RCTWM_WeightedLeave ||
            k.TangentWeightMode  == ERichCurveTangentWeightMode.RCTWM_WeightedBoth;
        bool arriveWeighted =
            k2.TangentWeightMode == ERichCurveTangentWeightMode.RCTWM_WeightedArrive ||
            k2.TangentWeightMode == ERichCurveTangentWeightMode.RCTWM_WeightedBoth;

        double rawM1 = k.LeaveTangent;
        double rawM2 = k2.ArriveTangent;

        double tP = leaveWeighted
            ? k.LeaveTangentWeight   / Math.Sqrt(1.0 + rawM1 * rawM1)
            : 1.0 / 3.0;

        double tQ = arriveWeighted
            ? k2.ArriveTangentWeight / Math.Sqrt(1.0 + rawM2 * rawM2)
            : 1.0 / 3.0;

        double v0 = k.Value  * cf;
        double v1 = k2.Value * cf;
        double m1 = rawM1 * cf;
        double m2 = rawM2 * cf;

        double bp1 = v0 + m1 * tP * dt;
        double bp2 = v1 - m2 * tQ * dt;

        double A = 3.0 * tP + 3.0 * tQ - 2.0;
        double B = 3.0 - 3.0 * tQ - 6.0 * tP;
        double C = 3.0 * tP;

        for (int step = 1; step < PointsPerSegment; step++)
        {
            double alpha = step / (double)PointsPerSegment;
            double u     = SolveCubicBezierU(A, B, C, alpha);
            double val   = BezierInterp(v0, bp1, bp2, v1, u);
            timePts.Add(k.Time + alpha * dt);
            valuePts.Add(val);
        }
    }

    private static double SolveCubicBezierU(double A, double B, double C, double alpha)
    {
        if (Math.Abs(A) < 1e-12 && Math.Abs(B) < 1e-12)
            return Math.Abs(C) > 1e-12 ? alpha / C : alpha;

        double u = alpha;
        for (int iter = 0; iter < 32; iter++)
        {
            double u2 = u * u;
            double f  = A * u2 * u + B * u2 + C * u - alpha;
            double df = 3.0 * A * u2 + 2.0 * B * u + C;
            if (Math.Abs(df) < 1e-14) break;
            double du = f / df;
            u -= du;
            if (Math.Abs(du) < 1e-12) break;
        }
        return Math.Max(0.0, Math.Min(1.0, u));
    }

    private static double BezierInterp(double p0, double p1, double p2, double p3, double u)
    {
        double omu = 1.0 - u;
        return omu * omu * omu * p0
             + 3.0 * omu * omu * u * p1
             + 3.0 * omu * u   * u * p2
             + u   * u   * u   * p3;
    }
}
