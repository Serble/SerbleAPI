using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SerbleAPI.Authentication;
using SerbleAPI.Data.Schemas;
using SerbleAPI.Repositories;

namespace SerbleAPI.API.v1.Payments;

[ApiController]
[Route("api/v1/products")]
[Authorize]
public class ProductsController(IUserRepository userRepo, IProductRepository productRepo) : ControllerManager {

    [HttpGet]
    public async Task<ActionResult<string[]>> Get() {
        User? target = await HttpContext.User.GetUser(userRepo);
        if (target == null) return Unauthorized();
        return await productRepo.GetOwnedProducts(target.Id);
    }
}