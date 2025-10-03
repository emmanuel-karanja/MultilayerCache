using Microsoft.AspNetCore.Mvc;
using MultilayerCache.Cache;
using MultilayerCache.Demo;

[ApiController]
[Route("api/[controller]")]
public class UsersController : ControllerBase
{
    private readonly InstrumentedCacheManagerDecorator<string, User> _cache;

    public UsersController(InstrumentedCacheManagerDecorator<string, User> cache)
    {
        _cache = cache;
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetUser(int id)
    {
        string key = $"user:{id}";
        var user = await _cache.GetOrAddAsync(key);
        if (user == null) return NotFound();

        return Ok(new
        {
            user.Id,
            user.Name,
            user.Email,
            LatencyMs = _cache.GetLatencyPerKey(key)
        });
    }

    [HttpPost]
    public async Task<IActionResult> CreateUser([FromBody] User user)
    {
        string key = $"user:{user.Id}";
        await _cache.SetAsync(key, user);
        return CreatedAtAction(nameof(GetUser), new { id = user.Id }, user);
    }
}
