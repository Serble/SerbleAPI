using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SerbleAPI.Authentication;
using SerbleAPI.Data;
using SerbleAPI.Data.Schemas;
using SerbleAPI.Repositories;

namespace SerbleAPI.API.v1.Account;

[Route("api/v1/account/products")]
[Controller]
[Authorize(Policy = "Scope:PaymentInfo")]
public class AccountProductsController(IUserRepository userRepo, IProductRepository productRepo) : ControllerManager {

    [HttpGet]
    public async Task<ActionResult<SerbleProduct>> Get() {
        User? target = await HttpContext.User.GetUser(userRepo);
        if (target == null) return Unauthorized();
        return Ok(await ProductManager.ListOfProductsFromUser(target, productRepo));
    }
}
