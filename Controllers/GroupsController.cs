using Data.Sqlite;
using Microsoft.AspNetCore.Mvc;
using Shared.Models;

namespace VanadiumAPI.Controllers
{
    [ApiController]
    [Route("api/groups")]
    public class GroupsController : ControllerBase
    {
        private readonly IPanelInfoRepository _repository;

        public GroupsController(IPanelInfoRepository repository) => _repository = repository;

        [HttpGet]
        public async Task<ActionResult<IEnumerable<Group>>> GetAllGroups()
        {
            try { return Ok(await _repository.GetAllGroups()); }
            catch (Exception ex) { return StatusCode(500, new { message = "Error retrieving groups", error = ex.Message }); }
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<Group>> GetGroupById(int id)
        {
            var group = await _repository.GetGroupById(id);
            if (group == null) return NotFound(new { message = "Group not found" });
            return Ok(group);
        }

        [HttpGet("enterprise/{id}")]
        public async Task<ActionResult<IEnumerable<Group>>> GetEnterpriseGroups(int id)
        {
            try { return Ok(await _repository.GetEnterpriseGroups(id)); }
            catch (Exception ex) { return StatusCode(500, new { message = "Error retrieving groups", error = ex.Message }); }
        }

        [HttpPost]
        public async Task<ActionResult<Group>> CreateGroup([FromBody] Group group)
        {
            if (group == null) return BadRequest(new { message = "Group data is required" });
            _repository.Add(group);
            if (await _repository.SaveAll()) return CreatedAtAction(nameof(GetGroupById), new { id = group.Id }, group);
            return BadRequest(new { message = "Failed to create group" });
        }

        [HttpDelete("{id}")]
        public async Task<ActionResult> DeleteGroup(int id)
        {
            var group = await _repository.GetGroupById(id);
            if (group == null) return NotFound(new { message = "Group not found" });
            _repository.Delete(group);
            if (await _repository.SaveAll()) return NoContent();
            return BadRequest(new { message = "Failed to delete group" });
        }
    }
}
