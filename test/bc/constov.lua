function expect(value, expected)
  assert(value == expected, tostring(expected) .. " expected, got " .. tostring(value))
end

do --- float
  local t = { "local x\n" }
  for i=2,128000 do t[i] = "x="..i..".5\n" end
  assert(loadstring(table.concat(t)) ~= nil)
  t[128001] = "x=128001.5"
  assert(loadstring(table.concat(t)) ~= nil)
end

do --- int
  local t = { "local x\n" }
  for i=2,128000 do t[i] = "x='"..i.."'\n" end
  assert(loadstring(table.concat(t)) ~= nil)
  t[128001] = "x='128001'"
  assert(loadstring(table.concat(t)) ~= nil)
end

do --- vcall
  local code = { "function f() return 1,2,3 end\nlocal t = {" }
  for i =1,128000 do
    code[#code+1] = "{" .. i .. "},"
  end
  code[#code+1] = "f() }\nreturn t"
  assert(loadstring(table.concat(code)) == nil)
  --[[local result = loadstring(code)()
  expect(type(result), "table")
  expect(#result, 128003)
  expect(result[128001], 1)
  expect(result[128002], 2)
  expect(result[128003], 3)]]
end