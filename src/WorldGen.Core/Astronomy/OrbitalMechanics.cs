using System;
using System.Runtime.CompilerServices;
using WorldGen.Core.Numerics;

namespace WorldGen.Core.Astronomy
{
    /// <summary>
    /// M3 pálya-, tengelydőlés- és forgás-számítások: nap-irány a bolygó
    /// saját (forgó, dőlt) koordinátarendszerében, szub-napponti pont,
    /// inszoláció (§27).
    ///
    /// HATÓKÖR (tudatosan szűkítve M3-ra — ld. tools/reference/astronomy_ref.py):
    ///   - Kör pálya (excentricitás = 0).
    ///   - Egyetlen csillag.
    ///
    /// Koordinátarendszerek:
    ///   - PÁLYA-KERET (nem forgó): XY sík = pályasík, Z = pálya-normális.
    ///   - TEST-KERET (a bolygóval együtt forgó, dőlt): ide kell a nap-irányt
    ///     számolni — ez határozza meg, mit lát a Unity Directional Light-ja.
    ///
    /// ND-27 LEZÁRVA: a forgatási mátrixok és a pálya-irány
    /// <see cref="DeterministicMath"/> SinCos-t használnak (Math.Sin/Cos
    /// helyett) — platformfüggetlenül bitpontos.
    ///
    /// KIVÉTEL — <see cref="SubsolarPoint"/>: Math.Asin/Atan2-t használ,
    /// SZÁNDÉKOSAN NEM cserélve. Ez a függvény jelenleg sehol nincs
    /// bekötve szimulációs kritikus útra (csak tesztekben hívott — a
    /// tényleges inszoláció-számítás, <see cref="Insolation"/>, közvetlen
    /// pontszorzatot használ, nem megy át szélesség/hosszúság
    /// koordinátán). Ha ez változik (pl. egy jövőbeli panel-mező innen
    /// olvas és checkpointolódik), az ND-27 kockázati osztálya és M12
    /// előtti lezárási kötelezettsége ide is kiterjed.
    /// </summary>
    public static class OrbitalMechanics
    {
        private readonly struct Matrix3
        {
            public readonly double M00, M01, M02;
            public readonly double M10, M11, M12;
            public readonly double M20, M21, M22;

            public Matrix3(
                double m00, double m01, double m02,
                double m10, double m11, double m12,
                double m20, double m21, double m22)
            {
                M00 = m00; M01 = m01; M02 = m02;
                M10 = m10; M11 = m11; M12 = m12;
                M20 = m20; M21 = m21; M22 = m22;
            }

            public static Matrix3 RotX(double angle)
            {
                DeterministicMath.SinCos(angle, out double s, out double c);
                return new Matrix3(
                    1, 0, 0,
                    0, c, -s,
                    0, s, c);
            }

            public static Matrix3 RotZ(double angle)
            {
                DeterministicMath.SinCos(angle, out double s, out double c);
                return new Matrix3(
                    c, -s, 0,
                    s, c, 0,
                    0, 0, 1);
            }

            public static Matrix3 Multiply(in Matrix3 a, in Matrix3 b)
            {
                return new Matrix3(
                    a.M00 * b.M00 + a.M01 * b.M10 + a.M02 * b.M20,
                    a.M00 * b.M01 + a.M01 * b.M11 + a.M02 * b.M21,
                    a.M00 * b.M02 + a.M01 * b.M12 + a.M02 * b.M22,

                    a.M10 * b.M00 + a.M11 * b.M10 + a.M12 * b.M20,
                    a.M10 * b.M01 + a.M11 * b.M11 + a.M12 * b.M21,
                    a.M10 * b.M02 + a.M11 * b.M12 + a.M12 * b.M22,

                    a.M20 * b.M00 + a.M21 * b.M10 + a.M22 * b.M20,
                    a.M20 * b.M01 + a.M21 * b.M11 + a.M22 * b.M21,
                    a.M20 * b.M02 + a.M21 * b.M12 + a.M22 * b.M22);
            }

            public Matrix3 Transposed()
            {
                return new Matrix3(
                    M00, M10, M20,
                    M01, M11, M21,
                    M02, M12, M22);
            }

            public void MultiplyVector(double x, double y, double z, out double rx, out double ry, out double rz)
            {
                rx = M00 * x + M01 * y + M02 * z;
                ry = M10 * x + M11 * y + M12 * z;
                rz = M20 * x + M21 * y + M22 * z;
            }
        }

        /// <summary>A bolygó szöghelyzete a pályán (kör pálya -> szög == mean anomaly).</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double OrbitalAngle(double t, double orbitalPeriod, double phase0 = 0.0)
        {
            return phase0 + 2.0 * Math.PI * (t / orbitalPeriod);
        }

        /// <summary>Egységvektor: bolygó -&gt; csillag irány, a nem-forgó pálya-keretben.</summary>
        public static void SunDirectionOrbitalFrame(
            double t, double orbitalPeriod, double phase0,
            out double x, out double y, out double z)
        {
            double theta = OrbitalAngle(t, orbitalPeriod, phase0);
            DeterministicMath.SinCos(theta, out double sinTheta, out double cosTheta);
            x = -cosTheta;
            y = -sinTheta;
            z = 0.0;
        }

        private static Matrix3 BodyOrientationMatrix(double axialTilt, double rotationAngle)
        {
            Matrix3 rTilt = Matrix3.RotX(axialTilt);
            Matrix3 rSpin = Matrix3.RotZ(rotationAngle);
            // Test -> pálya = R_tilt * R_spin; nekünk az inverze kell:
            // pálya -> test = (R_tilt * R_spin)^T = R_spin^T * R_tilt^T.
            Matrix3 bodyToOrbital = Matrix3.Multiply(rTilt, rSpin);
            return bodyToOrbital.Transposed();
        }

        /// <summary>A nap iránya a bolygó SAJÁT (forgó, dőlt) koordinátarendszerében.</summary>
        public static void SunDirectionBodyFrame(
            double t, double orbitalPeriod, double rotationPeriod, double axialTilt,
            double orbitalPhase0, double rotationPhase0,
            out double x, out double y, out double z)
        {
            SunDirectionOrbitalFrame(t, orbitalPeriod, orbitalPhase0, out double sx, out double sy, out double sz);
            double rotationAngle = rotationPhase0 + 2.0 * Math.PI * (t / rotationPeriod);
            Matrix3 m = BodyOrientationMatrix(axialTilt, rotationAngle);
            m.MultiplyVector(sx, sy, sz, out x, out y, out z);
        }

        /// <summary>Nap-irány (test-keret) -&gt; (szélesség, hosszúság) radiánban.</summary>
        public static void SubsolarPoint(double x, double y, double z, out double latitude, out double longitude)
        {
            double clamped = Math.Max(-1.0, Math.Min(1.0, z));
            latitude = Math.Asin(clamped);
            longitude = Math.Atan2(y, x);
        }

        /// <summary>§27: Insolation = F * max(0, cos theta).</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double Insolation(
            double sunX, double sunY, double sunZ,
            double normalX, double normalY, double normalZ,
            double flux)
        {
            double cosTheta = sunX * normalX + sunY * normalY + sunZ * normalZ;
            return flux * Math.Max(0.0, cosTheta);
        }

        /// <summary>§7.1: F = L / (4 * pi * r^2).</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double StellarFlux(double luminosity, double distance)
        {
            return luminosity / (4.0 * Math.PI * distance * distance);
        }
    }
}
