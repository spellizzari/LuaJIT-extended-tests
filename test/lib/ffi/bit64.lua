local function check(got, expected)
  if got ~= expected then
    error("got: \""..got.."\"\nexpected: \""..expected.."\"", 2)
  end
end

local ffi = require("ffi")
local bit = require("bit")

local tobit, bnot, bswap = bit.tobit, bit.bnot, bit.bswap
local band, bor, bxor = bit.band, bit.bor, bit.bxor
local shl, shr, sar = bit.lshift, bit.rshift, bit.arshift
local rol, ror = bit.rol, bit.ror

ffi.cdef[[
typedef enum { ZZI = -1 } ienum_t;
typedef enum { ZZU } uenum_t;
]]

do --- smoke tobit
  check(tobit(0xfedcba9876543210ll), 0x76543210)
  check(tobit(0xfedcba9876543210ull), 0x76543210)
end

do --- smoke band
  check(tostring(band(1ll, 1, 1ll, -1)), "1LL")
  check(tostring(band(1ll, 1, 1ull, -1)), "0ULL")
end

do --- smoke shl
  check(shl(10ll, 2), 40)
  check(shl(10, 2ll), 40)
  check(shl(10ll, 2ll), 40)
end

do --- smoke tohex
  check(bit.tohex(0x123456789abcdef0LL), "123456789abcdef0")
end

do --- tobit/band assorted C types
  for _,tp in ipairs{"int", "ienum_t", "uenum_t", "int64_t", "uint64_t"} do
    local x = ffi.new(tp, 10)
    local y = tobit(x)
    local z = band(x)
    assert(type(y) == "number" and y == 10)
    assert(type(z) == "cdata" and z == 10)
  end
end

do --- tobit/band negative unsigned enum
  local x = ffi.new("uenum_t", -10)
  local y = tobit(x)
  local z = band(x)
  check(type(y), "number")
  check(y, -10)
  check(type(z), "cdata")
  check(z, 2^32-10)
end

do --- jit band/bor/bxor
  local a = 0x123456789abcdef0LL
  local y1, y2, y3, y4, y5, y6
  for i=1,100 do
    y1 = band(a, 0x000000005a5a5a5aLL)
    y2 = band(a, 0x5a5a5a5a00000000LL)
    y3 = band(a, 0xffffffff5a5a5a5aLL)
    y4 = band(a, 0x5a5a5a5affffffffLL)
    y5 = band(a, 0xffffffff00000000LL)
    y6 = band(a, 0x00000000ffffffffLL)
  end
  check(y1, 0x000000001a185a50LL)
  check(y2, 0x1210525800000000LL)
  check(y3, 0x123456781a185a50LL)
  check(y4, 0x121052589abcdef0LL)
  check(y5, 0x1234567800000000LL)
  check(y6, 0x000000009abcdef0LL)
  for i=1,100 do
    y1 = bor(a, 0x000000005a5a5a5aLL)
    y2 = bor(a, 0x5a5a5a5a00000000LL)
    y3 = bor(a, 0xffffffff5a5a5a5aLL)
    y4 = bor(a, 0x5a5a5a5affffffffLL)
    y5 = bor(a, 0xffffffff00000000LL)
    y6 = bor(a, 0x00000000ffffffffLL)
  end
  check(y1, 0x12345678dafedefaLL)
  check(y2, 0x5a7e5e7a9abcdef0LL)
  check(y3, 0xffffffffdafedefaLL)
  check(y4, 0x5a7e5e7affffffffLL)
  check(y5, 0xffffffff9abcdef0LL)
  check(y6, 0x12345678ffffffffLL)
  for i=1,100 do
    y1 = bxor(a, 0x000000005a5a5a5aLL)
    y2 = bxor(a, 0x5a5a5a5a00000000LL)
    y3 = bxor(a, 0xffffffff5a5a5a5aLL)
    y4 = bxor(a, 0x5a5a5a5affffffffLL)
    y5 = bxor(a, 0xffffffff00000000LL)
    y6 = bxor(a, 0x00000000ffffffffLL)
  end
  check(y1, 0x12345678c0e684aaLL)
  check(y2, 0x486e0c229abcdef0LL)
  check(y3, 0xedcba987c0e684aaLL)
  check(y4, 0x486e0c226543210fLL)
  check(y5, 0xedcba9879abcdef0LL)
  check(y6, 0x123456786543210fLL)
end

do --- jit shift/xor
  local a, b = 0x123456789abcdef0LL, 0x31415926535898LL
  for i=1,200 do
    a = bxor(a, b); b = sar(b, 14) + shl(b, 50)
    a = a - b; b = shl(b, 5) + sar(b, 59)
    b = bxor(a, b); b = b - shl(b, 13) - shr(b, 51)
  end
  check(b, -7993764627526027113LL)
end

do --- jit rotate/xor
  local a, b = 0x123456789abcdef0LL, 0x31415926535898LL
  for i=1,200 do
    a = bxor(a, b); b = rol(b, 14)
    a = a - b; b = rol(b, 5)
    b = bxor(a, b); b = b - rol(b, 13)
  end
  check(b, -6199148037344061526LL)
end

do --- jit all ops
  local a, b = 0x123456789abcdef0LL, 0x31415926535898LL
  for i=1,200 do
    a = bxor(a, b); b = rol(b, a)
    a = a - b; b = shr(b, a) + shl(b, bnot(a))
    b = bxor(a, b); b = b - bswap(b)
  end
  check(b, -8881785180777266821LL)
end

