using System;
using WorldGen.Core.Random;

namespace WorldGen.Core.Tectonics
{
    /// <summary>
    /// M10 lemezmozgás (§14.2, docs/05-milestones.md §10.1): Euler-pólus +
    /// szögsebesség, Rodrigues-forgatás — <c>P(t) = R(ωt)P₀</c>.
    ///
    /// HATÓKÖR (tudatosan szűkítve): csak a lemez-MAG pozíciója mozog
    /// időben — erózió, eljegesedés, dinamikus tengerszint, lemez-
    /// születés/-halál halasztva (ld. milestones "M10 hatókör").
    ///
    /// IDŐEGYSÉG: millió év (Myr) — külön a csillagászat/klíma "nap"
    /// (dayT) tengelyétől.
    ///
    /// ND-27 KITERJESZTVE (nem új döntés): a Rodrigues-forgatás
    /// Math.Sin/Cos-t használ — ugyanaz a trigonometria-kockázati
    /// kategória és határidő (M12), mint a hőmérséklet-modellnél.
    /// </summary>
    public static class PlateMotion
    {
        public const double AngularVelocityMinRadPerMyr = 0.01;
        public const double AngularVelocityMaxRadPerMyr = 0.09;

        /// <summary>A lemez forgástengelye — egységvektor.</summary>
        public static void GenerateEulerPole(ulong worldSeed, int plateId, out double x, out double y, out double z)
        {
            DeterministicRandom.SampleUnitVector3(
                worldSeed, RandomDomain.Tectonics, (ulong)plateId, 0,
                out x, out y, out z, RandomProperty.EulerPole);
        }

        /// <summary>A lemez szögsebessége, rad/Myr.</summary>
        public static double GenerateAngularVelocity(ulong worldSeed, int plateId)
        {
            return DeterministicRandom.SampleRange(
                worldSeed, RandomDomain.Tectonics, (ulong)plateId, 0,
                AngularVelocityMinRadPerMyr, AngularVelocityMaxRadPerMyr,
                RandomProperty.PlateVelocity);
        }

        /// <summary>Rodrigues-forgatás: v elforgatva 'axis' körül 'angle' szöggel.</summary>
        public static void RodriguesRotate(
            double vx, double vy, double vz,
            double kx, double ky, double kz,
            double angle,
            out double rx, out double ry, out double rz)
        {
            double cosA = Math.Cos(angle);
            double sinA = Math.Sin(angle);

            double crossX = ky * vz - kz * vy;
            double crossY = kz * vx - kx * vz;
            double crossZ = kx * vy - ky * vx;

            double dot = kx * vx + ky * vy + kz * vz;

            rx = vx * cosA + crossX * sinA + kx * dot * (1.0 - cosA);
            ry = vy * cosA + crossY * sinA + ky * dot * (1.0 - cosA);
            rz = vz * cosA + crossZ * sinA + kz * dot * (1.0 - cosA);
        }

        /// <summary>A lemez-mag pozíciója <paramref name="timeMyr"/> időpontban.</summary>
        public static void PlateSeedAtTime(
            ulong worldSeed, int plateId,
            double seedX0, double seedY0, double seedZ0, double timeMyr,
            out double x, out double y, out double z)
        {
            GenerateEulerPole(worldSeed, plateId, out double axisX, out double axisY, out double axisZ);
            double omega = GenerateAngularVelocity(worldSeed, plateId);
            double angle = omega * timeMyr;

            RodriguesRotate(seedX0, seedY0, seedZ0, axisX, axisY, axisZ, angle, out x, out y, out z);
        }
    }
}
