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
    public ActionResult<string[]> Get() {
        User? target = HttpContext.User.GetUser(userRepo);
        if (target == null) return Unauthorized();
        return productRepo.GetOwnedProducts(target.Id);
    }
}