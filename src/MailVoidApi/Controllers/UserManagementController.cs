using MailVoidApi.Services;
using MailVoidWeb.Data.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MailVoidApi.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class UserManagementController : ControllerBase
{
    private readonly UserManagementService _userManagementService;
    private readonly AuthService _authService;

    public UserManagementController(UserManagementService userManagementService, AuthService authService)
    {
        _userManagementService = userManagementService;
        _authService = authService;
    }

    [HttpGet]
    public async Task<ActionResult<List<UserDto>>> GetAllUsers()
    {
        // Only admins can view all users
        if (!IsAdmin())
        {
            return Forbid();
        }

        var users = await _userManagementService.GetAllUsersAsync();
        var userDtos = users.Select(u => new UserDto
        {
            Id = u.Id.ToString(),
            UserName = u.UserName,
            Role = u.Role,
            TimeStamp = u.TimeStamp
        }).ToList();

        return Ok(userDtos);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<UserDto>> GetUser(Guid id)
    {
        // Users can view their own profile, admins can view any profile
        var currentUserId = GetCurrentUserId();
        if (!IsAdmin() && currentUserId != id)
        {
            return Forbid();
        }

        var user = await _userManagementService.GetUserByIdAsync(id);
        if (user == null)
        {
            return NotFound();
        }

        var userDto = new UserDto
        {
            Id = user.Id.ToString(),
            UserName = user.UserName,
            Role = user.Role,
            TimeStamp = user.TimeStamp
        };

        return Ok(userDto);
    }

    [HttpPost]
    public async Task<ActionResult<UserDto>> CreateUser(CreateUserDto createUserDto)
    {
        // Only admins can create users
        if (!IsAdmin())
        {
            return Forbid();
        }

        if (string.IsNullOrWhiteSpace(createUserDto.UserName) ||
            string.IsNullOrWhiteSpace(createUserDto.Password))
        {
            return BadRequest("Username and password are required.");
        }

        if (await _userManagementService.UserExistsAsync(createUserDto.UserName))
        {
            return BadRequest("Username already exists.");
        }

        var user = await _userManagementService.CreateUserAsync(
            createUserDto.UserName,
            createUserDto.Password,
            createUserDto.Role);

        if (user == null)
        {
            return BadRequest("Failed to create user.");
        }

        var userDto = new UserDto
        {
            Id = user.Id.ToString(),
            UserName = user.UserName,
            Role = user.Role,
            TimeStamp = user.TimeStamp
        };

        return CreatedAtAction(nameof(GetUser), new { id = user.Id }, userDto);
    }

    [HttpPut("{id}/role")]
    public async Task<ActionResult> UpdateUserRole(Guid id, UpdateRoleDto updateRoleDto)
    {
        // Only admins can change roles
        if (!IsAdmin())
        {
            return Forbid();
        }

        var success = await _userManagementService.UpdateUserRoleAsync(id, updateRoleDto.Role);
        if (!success)
        {
            return NotFound();
        }

        return NoContent();
    }

    [HttpPut("{id}/password")]
    public async Task<ActionResult> UpdateUserPassword(Guid id, UpdatePasswordDto updatePasswordDto)
    {
        // Users can change their own password, admins can change any password
        var currentUserId = GetCurrentUserId();
        if (!IsAdmin() && currentUserId != id)
        {
            return Forbid();
        }

        if (string.IsNullOrWhiteSpace(updatePasswordDto.NewPassword))
        {
            return BadRequest("New password is required.");
        }

        var success = await _userManagementService.UpdateUserPasswordAsync(id, updatePasswordDto.NewPassword);
        if (!success)
        {
            return NotFound();
        }

        return NoContent();
    }

    [HttpDelete("{id}")]
    public async Task<ActionResult> DeleteUser(Guid id)
    {
        // Only admins can delete users
        if (!IsAdmin())
        {
            return Forbid();
        }

        // Prevent admins from deleting themselves
        var currentUserId = GetCurrentUserId();
        if (currentUserId == id)
        {
            return BadRequest("Cannot delete your own account.");
        }

        var success = await _userManagementService.DeleteUserAsync(id);
        if (!success)
        {
            return NotFound();
        }

        return NoContent();
    }

    private bool IsAdmin()
    {
        var role = _authService.GetUserRoleFromPrincipal(User);
        return role == Role.Admin.ToString();
    }

    private Guid? GetCurrentUserId()
    {
        return _authService.GetUserIdFromPrincipal(User);
    }
}

public class UserDto
{
    public string Id { get; set; } = string.Empty;
    public required string UserName { get; set; }
    public Role Role { get; set; }
    public DateTime TimeStamp { get; set; }
}

public class CreateUserDto
{
    public required string UserName { get; set; }
    public required string Password { get; set; }
    public Role Role { get; set; } = Role.User;
}

public class UpdateRoleDto
{
    public Role Role { get; set; }
}

public class UpdatePasswordDto
{
    public required string NewPassword { get; set; }
}
