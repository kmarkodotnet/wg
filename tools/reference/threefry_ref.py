"""
Threefry-4x64-20 referencia-implementáció.
Cél: a C# implementáció verifikálása és a tesztvektorok generálása.
Ez NEM produkciós kód — csak orákulum.
"""

M64 = (1 << 64) - 1
SKEIN_KS_PARITY = 0x1BD11BDAA9FC1A22

# Random123 rotációs konstansok, Threefry4x64
ROT = [
    (14, 16),
    (52, 57),
    (23, 40),
    (5, 37),
    (25, 33),
    (46, 12),
    (58, 22),
    (32, 32),
]


def rotl(x, n):
    x &= M64
    return ((x << n) | (x >> (64 - n))) & M64


def threefry4x64(ctr, key, rounds=20):
    """ctr, key: 4 db 64 bites egész. Kimenet: 4 db 64 bites egész."""
    ks = [k & M64 for k in key]
    parity = SKEIN_KS_PARITY
    for k in ks:
        parity ^= k
    ks.append(parity)

    x = [(ctr[i] + ks[i]) & M64 for i in range(4)]

    for r in range(rounds):
        rot0, rot1 = ROT[r % 8]
        if r % 2 == 0:
            # (0,1) és (2,3) párosítás
            x[0] = (x[0] + x[1]) & M64
            x[1] = rotl(x[1], rot0) ^ x[0]
            x[2] = (x[2] + x[3]) & M64
            x[3] = rotl(x[3], rot1) ^ x[2]
        else:
            # (0,3) és (2,1) párosítás
            x[0] = (x[0] + x[3]) & M64
            x[3] = rotl(x[3], rot0) ^ x[0]
            x[2] = (x[2] + x[1]) & M64
            x[1] = rotl(x[1], rot1) ^ x[2]

        # kulcs-injekció minden 4. kör után
        if r % 4 == 3:
            inj = r // 4 + 1
            for i in range(4):
                x[i] = (x[i] + ks[(inj + i) % 5]) & M64
            x[3] = (x[3] + inj) & M64

    return x


# ---- Verifikáció a publikált Random123 tesztvektorokhoz ----

KNOWN = [
    (
        "zeros",
        [0, 0, 0, 0],
        [0, 0, 0, 0],
        [0x09218EBDE6C85537, 0x55941F5266D86105,
         0x4BD25E16282434DC, 0xEE29EC846BD2E40B],
    ),
    (
        "ones",
        [M64] * 4,
        [M64] * 4,
        [0x29C24097942BBA1B, 0x0371BBFB0F6F4E11,
         0x3C231FFA33F83A1C, 0xCD29113FDE32D168],
    ),
    (
        "pi",
        [0x243F6A8885A308D3, 0x13198A2E03707344,
         0xA4093822299F31D0, 0x082EFA98EC4E6C89],
        [0x452821E638D01377, 0xBE5466CF34E90C6C,
         0xC0AC29B7C97C50DD, 0x3F84D5B5B5470917],
        [0xA7E8FDE591651BD9, 0xBAAFD0C30138319B,
         0x84A5C1A729E685B9, 0x901D406CCEBC1BA4],
    ),
]

if __name__ == "__main__":
    ok = True
    for name, ctr, key, expected in KNOWN:
        got = threefry4x64(ctr, key, 20)
        match = got == expected
        ok &= match
        print(f"[{'OK ' if match else 'HIBA'}] {name}")
        if not match:
            print("   várt :", " ".join(f"{v:016x}" for v in expected))
            print("   kapott:", " ".join(f"{v:016x}" for v in got))
    print()
    print("Összesítés:", "MINDEN VEKTOR EGYEZIK" if ok else "ELTÉRÉS VAN")
