using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NeuroSync.Data;
using NeuroSync.Models;

namespace NeuroSync.Controllers;

/// <summary>
/// Controlador responsável pela autenticação e controle de sessão da usuária no sistema.
/// </summary>
public class LoginController(AppDbContext context) : Controller
//faz a injeção de dependências e cria a variavel _context automaticamente.
{
    
    [HttpGet]
    public IActionResult Index() => View();

    [HttpPost]
    public async Task<IActionResult> Entrar(string usuario, string senha)
    {
        if (string.IsNullOrWhiteSpace(usuario) || string.IsNullOrWhiteSpace(senha))
        //caso tenha vindo algua informação nula do formulario, já retorna o erro.
        {
            ViewBag.Erro = "Por favor, informe o usuário/e-mail e a senha.";
            return View("Index");
        }

        var termo = usuario.Trim();

        // Localiza usuário por e-mail ou nome
        //context.Usuarios: referencia a tabela do banco de dados.
        //FirstouDefaultAsync: busca o prieiro registro correspondente no db, ou nulo se nao houver.
        //await: impede que o programa trave enquanto a busca é feita.
        var usuarioEncontrado = await context.Usuarios
            .FirstOrDefaultAsync(u => (u.Email.ToLower() == termo.ToLower() || u.Nome.ToLower() == termo.ToLower()) && u.Senha == senha);
            //na função lambda, u é um parametro do tipo Usuario.
            //O arrow, => implicitamente retorna uma expressão booleana, que será testada com cada registro do banco de dados.
            // e caso satisfaça as condições, o registro será retornado ao UsuarioEncontrado.




        // Fallback de primeiro acesso: caso o banco esteja vazio, sem nenhum usuario, e ele tente digitar as credenciais de admin, o sistema cria esse usuário
        //impedindo que o sistema fique inutilizavel.
        if (usuarioEncontrado == null && termo.Equals("admin", StringComparison.OrdinalIgnoreCase) && senha == "admin123")
        {
            usuarioEncontrado = await context.Usuarios.FirstOrDefaultAsync();
            if (usuarioEncontrado == null)
            {
                usuarioEncontrado = new Usuario
                {
                    Nome = "Mariana Silva",
                    Email = "admin",
                    Senha = "admin123",
                    CriadoEm = DateTime.Now
                };
                context.Usuarios.Add(usuarioEncontrado);
                await context.SaveChangesAsync();
            }
        }

        //Cria as credenciais de autenticação do usuario caso ele tenha sido encontrado no banco de dados.
        if (usuarioEncontrado != null)
        {
            Claim[] claims = [
                new(ClaimTypes.NameIdentifier, usuarioEncontrado.IdUsuario.ToString()),
                new(ClaimTypes.Name, usuarioEncontrado.Nome),
                new(ClaimTypes.Email, usuarioEncontrado.Email)
            ];

            // o sistema informa ao navegador que o usuário foi autenticado, e cria um cookie de sessão para que ele se mantenha lgoado
            //ele tambem cripografa os dados (CookieAuthenticationDefaults.AuthenticationScheme) para que não seja possível ter acesso as credenciais.
            var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);

            await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));

            return RedirectToAction("BoasVindas", "Home");
        }

        ViewBag.Erro = "Usuário ou senha inválidos!";
        return View("Index");
    }
    public async Task<IActionResult> Sair()
    {
        await HttpContext.SignOutAsync();
        return RedirectToAction("Index", "Login");
    }
}