-- Read the current request count for the active window.
local current = redis.call('GET', KEYS[1])

-- If the limit is already reached, reject without incrementing the counter.
if current and tonumber(current) >= tonumber(ARGV[1]) then
    return { 0, redis.call('PTTL', KEYS[1]) }
end

-- Count this request.
local count = redis.call('INCR', KEYS[1])

-- INCR creates the key with value 1 if it did not exist.
-- Set the window expiration only for the first request.
if count == 1 then
    redis.call('PEXPIRE', KEYS[1], ARGV[2])
end

-- Request is allowed; also return the remaining window lifetime in ms.
return { 1, redis.call('PTTL', KEYS[1]) }