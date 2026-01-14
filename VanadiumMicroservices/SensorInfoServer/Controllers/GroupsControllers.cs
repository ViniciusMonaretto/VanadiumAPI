using Data.Sqlite;
using Microsoft.AspNetCore.Mvc;
using Shared.Models;

namespace SensorInfoServer.Controllers
{
     [ApiController]
    [Route("api/groups")]
    public class GroupsController : ControllerBase
    {
        private readonly IPanelInfoRepository _repository;

        public GroupsController(IPanelInfoRepository repository)
        {
            _repository = repository;
        }

        // GET: api/groups
        [HttpGet]
        public async Task<ActionResult<IEnumerable<Group>>> GetAllGroups()
        {
            try
            {
                var groups = await _repository.GetAllGroups();
                return Ok(groups);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Error retrieving groups", error = ex.Message });
            }
        }

        // GET: api/groups/5
        [HttpGet("{id}")]
        public async Task<ActionResult<Group>> GetGroupById(int id)
        {
            try
            {
                var group = await _repository.GetGroupById(id);
                
                if (group == null)
                    return NotFound(new { message = $"Group with id {id} not found" });

                return Ok(group);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Error retrieving group", error = ex.Message });
            }
        }

        // GET: api/groups/enterprise/5
        [HttpGet("enterprise/{id}")]
        public async Task<ActionResult<Group>> GetEnterpriseGroups(int id)
        {
            try
            {
                var group = await _repository.GetEnterpriseGroups(id);
                
                if (group == null)
                    return NotFound(new { message = $"Group with id {id} not found" });

                return Ok(group);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Error retrieving group", error = ex.Message });
            }
        }

        // POST: api/groups
        [HttpPost]
        public async Task<ActionResult<Group>> CreateGroup([FromBody] Group group)
        {
            try
            {
                if (group == null)
                    return BadRequest(new { message = "Group data is required" });

                _repository.Add(group);
                
                if (await _repository.SaveAll())
                    return CreatedAtAction(nameof(GetGroupById), new { id = group.Id }, group);

                return BadRequest(new { message = "Failed to create group" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Error creating group", error = ex.Message });
            }
        }

        // DELETE: api/groups/5
        [HttpDelete("{id}")]
        public async Task<ActionResult> DeleteGroup(int id)
        {
            try
            {
                var group = await _repository.GetGroupById(id);
                
                if (group == null)
                    return NotFound(new { message = $"Group with id {id} not found" });

                _repository.Delete(group);
                
                if (await _repository.SaveAll())
                    return NoContent();

                return BadRequest(new { message = "Failed to delete group" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Error deleting group", error = ex.Message });
            }
        }
    }
}