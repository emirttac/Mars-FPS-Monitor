using System;
using System.Collections.Generic;
using System.Linq;

namespace FPSOverlay
{
    /// <summary>Piecewise-linear temp → PWM with optional hold for hysteresis callers.</summary>
    public sealed class FanCurveEngine
    {
        public int Interpolate(FanCurve curve, float tempC)
        {
            var points = (curve.Points ?? new List<FanCurvePoint>())
                .Where(p => p != null)
                .OrderBy(p => p.TempC)
                .ToList();

            if (points.Count == 0)
                return 40;

            if (points.Count == 1 || tempC <= points[0].TempC)
                return ClampPwm(points[0].PwmPercent);

            if (tempC >= points[^1].TempC)
                return ClampPwm(points[^1].PwmPercent);

            for (int i = 0; i < points.Count - 1; i++)
            {
                var a = points[i];
                var b = points[i + 1];
                if (tempC > b.TempC) continue;
                float span = b.TempC - a.TempC;
                if (span <= 0.01f)
                    return ClampPwm(b.PwmPercent);
                float t = (tempC - a.TempC) / span;
                int pwm = (int)Math.Round(a.PwmPercent + t * (b.PwmPercent - a.PwmPercent));
                return ClampPwm(pwm);
            }

            return ClampPwm(points[^1].PwmPercent);
        }

        /// <summary>
        /// Hold last PWM when cooling down inside hysteresis, and ignore 1% flicker.
        /// </summary>
        public int ApplyHysteresis(int desired, int lastPwm, float tempC, float lastTempC, float hysteresisC)
        {
            if (Math.Abs(desired - lastPwm) < 2)
                return lastPwm;

            if (desired < lastPwm && tempC > lastTempC - Math.Max(0.5f, hysteresisC))
                return lastPwm;

            return desired;
        }

        private static int ClampPwm(int pwm) => Math.Clamp(pwm, 0, 100);
    }
}
