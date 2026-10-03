"""ND-143: a bináris checkpoint független struct/hashlib orákuluma (nulla kezdőállapot)."""
import hashlib
import json
import struct
import thermal_field_ref as ref


def u64(value):
    return struct.pack(">Q", value & ((1 << 64) - 1))


def f64(value):
    return struct.pack(">d", value)


def string(value):
    data = value.encode("utf-8")
    return u64(len(data)) + data


def main():
    ref.LEVEL, ref.N, ref.CELL_COUNT = 1, 2, 24
    # A codec-KAT bemenete ne függjön az ND-24 tan-warp platformfüggő sütésétől.
    kinds, elevation = [ref.LAND] * ref.CELL_COUNT, [0.0] * ref.CELL_COUNT
    # FIGYELEM: ennek egyeznie kell a C# `WorldGeneratorVersion.Current`-tel
    # (ND-108). Verzioemeleskor ITT is emelni kell ES ujra kell generalni a
    # vektorokat - kulonben a checkpoint-KAT elbukik (ez tortent az
    # ND-136 / A19 emelesnel, 2 -> 3, es az ND-137-nel, 3 -> 4 -> 5).
    generator = "8"
    inputs = string("WorldGen.Thermal.Inputs.1") + string(generator) + u64(ref.MODEL_VERSION)
    inputs += u64(ref.LEVEL) + f64(ref.RADIUS_M) + u64(1) + f64(0.0) + f64(0.0)
    inputs += f64(ref.ORBITAL_PERIOD_DAYS) + f64(ref.ROTATION_PERIOD_DAYS) + f64(ref.AXIAL_TILT_RAD)
    for v in (ref.F_PEAK, ref.AIR_HEAT_CAPACITY, ref.AIR_RELAXATION, ref.EXCHANGE_PER_MS,
              ref.MIN_EXCHANGE_WIND_MS, ref.RADIATIVE_SMOOTHING, ref.AIR_FEEDBACK_STRENGTH):
        inputs += f64(v)
    inputs += string("ND-174-seasonal-energy-balance") + u64(48)
    # ND-160: a BAZIS albedoja (ocean, szarazfold) - bolygo-albedoval mindketto 0,30.
    inputs += f64(ref.ALBEDO_PLANET_BASELINE) + f64(ref.ALBEDO_PLANET_BASELINE)
    # A paraméterobjektum dokumentált műveleti sorrendje: előbb fajhő, utána térfogati kapacitás.
    land_cs = ref.SOIL_DRY_DENSITY * (ref.SOIL_SPECIFIC_HEAT) * ref.LAND_DEPTH_M
    capacities = [land_cs, ref._OCEAN_CS, ref._OCEAN_CS, land_cs]
    for k in range(4):
        inputs += f64(ref.ALBEDO[k]) + f64(ref.EMISSIVITY[k]) + f64(capacities[k])
    for kind, height in zip(kinds, elevation):
        inputs += u64(kind) + f64(height)
    identity = hashlib.sha256(inputs).hexdigest()
    payload = u64(0x5747544830303031) + string(generator) + u64(ref.MODEL_VERSION) + string(identity)
    payload += u64(ref.CELL_COUNT) + u64(-960) + u64(-960)
    payload += (f64(0.0) + f64(0.0)) * ref.CELL_COUNT
    digest = hashlib.sha256(payload).digest()
    vectors = {"modelIdentity": identity, "stateHash": digest.hex(), "checkpointHex": (payload + digest).hex()}
    with open("thermal_checkpoint_vectors.json", "w", encoding="utf-8", newline="\n") as f:
        json.dump(vectors, f, indent=1)
    print("PASS: független big-endian checkpoint/hash vektor kiírva.")


if __name__ == "__main__":
    main()
