local current = redis.call('GET', KEYS[1])

if current and tonumber(current) >= tonumber(ARGV[1]) then
    return { 0, redis.call('PTTL', KEYS[1]) }
end

local count = redis.call('INCR', KEYS[1])

if count == 1 then
    redis.call('PEXPIRE', KEYS[1], ARGV[2])
end

return { 1, redis.call('PTTL', KEYS[1]) }