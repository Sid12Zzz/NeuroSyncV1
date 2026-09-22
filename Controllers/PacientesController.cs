    using System.Globalization;
    using Microsoft.AspNetCore.Authorization;
    using Microsoft.AspNetCore.Mvc;
    using Microsoft.EntityFrameworkCore;
    using NeuroSync.Data;
    using NeuroSync.Models;
    using QuestPDF.Fluent;
    using QuestPDF.Helpers;
    using QuestPDF.Infrastructure;

    namespace NeuroSync.Controllers;

    /// <summary>
    /// Controlador central para gestão dos Pacientes, Prontuário Eletrônico (PEP),
    /// Evoluções Clínicas, Pareceres Técnicos de Neuropsicopedagogia e Anexos.
    /// </summary>
    [Authorize]
    public class PacientesController(AppDbContext context, IWebHostEnvironment hostEnvironment) : Controller
    {
        // =========================================================================
        // 1. BUSCA E LISTAGEM DE PACIENTES
        // =========================================================================

        /// Lista os pacientes cadastrados com filtro dinâmico por termo de busca.
        public async Task<IActionResult> Index(string termoBusca)
        {
            var pacientes = context.Pacientes.AsQueryable();

            if (!string.IsNullOrWhiteSpace(termoBusca))
            {
                var buscaLower = termoBusca.ToLower();
                //faz a busca no banco de dados, tabela
                pacientes = pacientes.Where(p => p.Nome.ToLower().Contains(buscaLower));
                ViewBag.BuscaAtual = termoBusca;
            }

            return View(await pacientes.OrderBy(p => p.Nome).ToListAsync());
        }

        // =========================================================================
        // 2. PRONTUÁRIO CLÍNICO INTEGRADO (PEP)
        // =========================================================================


        /// Exibe a ficha completa do paciente: resumo, anamnese, evoluções, atendimentos, pareceres, cobranças e anexos.
        /// 
        /// Reúne todas as informações de um paciente (atendimentom, evolução, parecer....)
        /// 
        /// Ao entrar na aba details, busca um cliente pelo id.
        /// possui metodos que consultam via sql o banco de dados e retornamlistas de agendamentos e etc.
        /// por meio do viewbag, mandam um objeto para o frontend "desenha-los".
            public async Task<IActionResult> Details(int? id, string? aba = null)
        {

            //Valida se algum id foi informado.
            //busca o paciente na tabela do bd.
            if (id == null) return NotFound();

            var paciente = await context.Pacientes.FirstOrDefaultAsync(m => m.IdPaciente == id);
            if (paciente == null) return NotFound();

            // 1. Evoluções clínicas registradas
            //faz a busca no db de todas as evoluções deste paciente e guarda em uma lista.
            var evolucoes = await context.Evolucoes
                .Where(e => e.PacienteId == id)
                .OrderByDescending(e => e.DataRegistro)
                .ToListAsync();


            //viewbag: é um objeto dinâmico, que o html utiliza para desenhar os objetos na tela.
            ViewBag.Evolucoes = evolucoes;        
            ViewBag.UltimasEvolucoes = evolucoes;

            // 2. Histórico de agendamentos e próximos atendimentos

            var todosAgendamentos = await context.Agendamentos
                .Where(a => a.PacienteId == id)
                .OrderByDescending(a => a.DataHora)
                .ToListAsync();

            ViewBag.TodosAgendamentos = todosAgendamentos;
            ViewBag.Agendamentos = todosAgendamentos.Where(a => a.DataHora >= DateTime.Today).OrderBy(a => a.DataHora).ToList();
            ViewBag.ProximosAtendimentos = ViewBag.Agendamentos;

            // 3. Pareceres técnicos emitidos
            ViewBag.PareceresTecnicos = await context.PareceresTecnicos
                .Where(p => p.PacienteId == id)
                .OrderByDescending(p => p.DataEmissao)
                .ToListAsync();

            // 4. Cobranças e faturamento do paciente
            ViewBag.Cobrancas = await context.Cobrancas
                .Where(c => c.PacienteId == id)
                .OrderByDescending(c => c.DataVencimento)
                .ToListAsync();

            // 5. Arquivos e documentos anexados
            ViewBag.Anexos = await context.Anexos
                .Where(a => a.PacienteId == id)
                .OrderByDescending(a => a.DataUpload)
                .ToListAsync();

            //
            ViewBag.AbaAtiva = string.IsNullOrWhiteSpace(aba) ? "resumo" : aba.ToLower().Trim();

            return View(paciente);
        }

        // =========================================================================
        // 3. EVOLUÇÕES CLÍNICAS (REGISTRO, CONSULTA, IMPRESSÃO E PDF)
        // =========================================================================

 
        [HttpPost]
        //proteção contra ataques CSRF Cross-Site Request Forgery (um site malicioso envia uma requisição para um site veridico para que ele execute alguma ação (transferir dinheiro etc))
        //iterrompe a requisição e envia um erro 400.
        [ValidateAntiForgeryToken]

        public async Task<IActionResult> AdicionarEvolucao(int PacienteId, string Anotacao, string? TipoEvolucao, string? ProfissionalNome)
        {
            if (!string.IsNullOrWhiteSpace(Anotacao))
            {

                //verifica se o nome do profissional é nulo, caso não, atribui o informado, caso sim, atribui o usuario autenticado ou "Dra. Mariana Silva".
                var profissional = !string.IsNullOrWhiteSpace(ProfissionalNome)
                    ? ProfissionalNome.Trim()
                    : (User.Identity?.IsAuthenticated == true && !string.IsNullOrEmpty(User.Identity.Name) ? User.Identity.Name : "Dra. Mariana Silva");


                //Cria a evolucao.
                context.Evolucoes.Add(new Evolucao
                {
                    PacienteId = PacienteId,
                    Anotacao = Anotacao.Trim(),
                    TipoEvolucao = !string.IsNullOrWhiteSpace(TipoEvolucao) ? TipoEvolucao.Trim() : "Intervenção Cognitiva",
                    ProfissionalNome = profissional,
                    DataRegistro = DateTime.Now
                });

                //Realiza o insert into no banco de dados.
                await context.SaveChangesAsync();

                TempData["MensagemSucesso"] = "Evolução clínica registrada com sucesso!";
                TempData["MensagemSucessoEvolucao"] = "Evolução clínica registrada com sucesso!";
            }

            return RedirectToAction(nameof(Details), new { id = PacienteId, aba = "evolucao" });
        }

        /// <summary>
        /// Abre a tela de visualização individual e detalhada da evolução clínica.
        /// </summary>
        public async Task<IActionResult> VisualizarEvolucao(int id)
        {
            var evolucao = await context.Evolucoes
                .Include(e => e.Paciente)
                .FirstOrDefaultAsync(e => e.IdEvolucao == id);

            return evolucao == null ? NotFound() : View(evolucao);
        }

        /// <summary>
        /// Abre folha timbrada web pronta para impressão direta da evolução clínica.
        /// </summary>
        public async Task<IActionResult> ImprimirEvolucao(int id)
        {
            var evolucao = await context.Evolucoes
                .Include(e => e.Paciente)
                .FirstOrDefaultAsync(e => e.IdEvolucao == id);

            return evolucao == null ? NotFound() : View(evolucao);
        }

        /// <summary>
        /// Emite documento oficial em PDF da evolução clínica com identidade visual NeuroSync via QuestPDF.
        /// </summary>
        public async Task<IActionResult> GerarEvolucaoPdf(int id)
        {
            var evolucao = await context.Evolucoes
                .Include(e => e.Paciente)
                .FirstOrDefaultAsync(e => e.IdEvolucao == id);

            if (evolucao?.Paciente == null) return NotFound();

            var paciente = evolucao.Paciente;
            var culturaBr = new CultureInfo("pt-BR");

            var idade = DateTime.Today.Year - paciente.DataNascimento.Year;
            if (paciente.DataNascimento.Date > DateTime.Today.AddYears(-idade)) idade--;

            string responsavel = !string.IsNullOrWhiteSpace(paciente.Responsavel) ? paciente.Responsavel :
                                !string.IsNullOrWhiteSpace(paciente.NomeMae) ? paciente.NomeMae :
                                !string.IsNullOrWhiteSpace(paciente.NomePai) ? paciente.NomePai : "Não informado";

            string logoPath = Path.Combine(hostEnvironment.WebRootPath, "images", "logo-principal-cerebro-coracao 2.png");
            byte[]? logoBytes = System.IO.File.Exists(logoPath) ? System.IO.File.ReadAllBytes(logoPath) : null;

            var documento = Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(32);
                    page.DefaultTextStyle(x => x.FontSize(9.5f).FontColor(Color.FromHex("#1e293b")));

                    // 1. Cabeçalho Timbrado
                    page.Header().Column(headerCol =>
                    {
                        headerCol.Item().Row(row =>
                        {
                            row.RelativeItem(7).Row(bRow =>
                            {
                                if (logoBytes != null)
                                {
                                    bRow.ConstantItem(46).Height(46).Image(logoBytes).FitArea();
                                    bRow.ConstantItem(10);
                                }

                                bRow.RelativeItem().Column(brandCol =>
                                {
                                    brandCol.Item().Row(logoRow =>
                                    {
                                        logoRow.AutoItem().Text("Neuro").FontSize(20).Bold().FontColor(Color.FromHex("#071A3A"));
                                        logoRow.AutoItem().Text("Sync").FontSize(20).Bold().FontColor(Color.FromHex("#315BEF"));
                                    });
                                    brandCol.Item().Text("Clínica de Desenvolvimento e Neuropsicopedagogia").FontSize(8.5f).FontColor(Colors.Grey.Darken1);
                                    brandCol.Item().Text("Atendimento Especializado em Neuropsicopedagogia").FontSize(7.5f).FontColor(Colors.Grey.Medium);
                                });
                            });

                            row.RelativeItem(5).AlignRight().Column(metaCol =>
                            {
                                metaCol.Item().Text("REGISTRO DE EVOLUÇÃO").FontSize(12).Bold().FontColor(Color.FromHex("#071A3A"));
                                metaCol.Item().Text($"Registro: #EV{evolucao.IdEvolucao:D4}").FontSize(8.5f).FontColor(Color.FromHex("#315BEF")).Bold();
                                metaCol.Item().Text($"Data: {evolucao.DataRegistro:dd/MM/yyyy 'às' HH:mm}").FontSize(8.5f).FontColor(Colors.Grey.Darken2);
                            });
                        });

                        headerCol.Item().PaddingTop(8).LineHorizontal(2).LineColor(Color.FromHex("#315BEF"));
                    });

                    // 2. Corpo do Documento
                    page.Content().PaddingTop(16).Column(col =>
                    {
                        col.Item().Background(Color.FromHex("#F8FAFC")).Border(1).BorderColor(Color.FromHex("#E2E8F0")).Padding(12).Column(pCol =>
                        {
                            pCol.Item().Text("IDENTIFICAÇÃO DO PACIENTE").FontSize(8.5f).Bold().FontColor(Color.FromHex("#315BEF"));
                            pCol.Item().PaddingTop(4).Row(r =>
                            {
                                r.RelativeItem(6).Text(t => { t.Span("Nome do Paciente: ").Bold(); t.Span(paciente.Nome); });
                                r.RelativeItem(3).Text(t => { t.Span("Idade: ").Bold(); t.Span($"{idade} anos"); });
                                r.RelativeItem(3).Text(t => { t.Span("Prontuário: ").Bold(); t.Span($"#{paciente.IdPaciente:D4}"); });
                            });
                            pCol.Item().PaddingTop(4).Row(r =>
                            {
                                r.RelativeItem(6).Text(t => { t.Span("Responsável: ").Bold(); t.Span(responsavel); });
                                r.RelativeItem(6).Text(t => { t.Span("Data Nasc.: ").Bold(); t.Span(paciente.DataNascimento.ToString("dd/MM/yyyy")); });
                            });
                        });

                        col.Item().PaddingTop(12).Row(r =>
                        {
                            r.RelativeItem(6).Border(1).BorderColor(Color.FromHex("#E2E8F0")).Padding(10).Column(c =>
                            {
                                c.Item().Text("MODALIDADE CLÍNICA").FontSize(8).Bold().FontColor(Colors.Grey.Darken1);
                                c.Item().PaddingTop(2).Text(!string.IsNullOrWhiteSpace(evolucao.TipoEvolucao) ? evolucao.TipoEvolucao : "Intervenção Cognitiva").Bold().FontColor(Color.FromHex("#071A3A"));
                            });
                            r.ConstantItem(8);
                            r.RelativeItem(6).Border(1).BorderColor(Color.FromHex("#E2E8F0")).Padding(10).Column(c =>
                            {
                                c.Item().Text("PROFISSIONAL RESPONSÁVEL").FontSize(8).Bold().FontColor(Colors.Grey.Darken1);
                                c.Item().PaddingTop(2).Text(!string.IsNullOrWhiteSpace(evolucao.ProfissionalNome) ? evolucao.ProfissionalNome : "Dra. Mariana Silva").Bold().FontColor(Color.FromHex("#071A3A"));
                            });
                        });

                        col.Item().PaddingTop(16).Text("REGISTRO DESCRITIVO DA SESSÃO").FontSize(9.5f).Bold().FontColor(Color.FromHex("#071A3A"));
                        col.Item().PaddingTop(6).Border(1).BorderColor(Color.FromHex("#E2E8F0")).Padding(14).Text(evolucao.Anotacao).LineHeight(1.35f).FontSize(10);

                        col.Item().PaddingTop(24).AlignRight().Column(signCol =>
                        {
                            signCol.Item().Width(240).LineHorizontal(1).LineColor(Colors.Grey.Medium);
                            signCol.Item().PaddingTop(4).Text(!string.IsNullOrWhiteSpace(evolucao.ProfissionalNome) ? evolucao.ProfissionalNome : "Profissional Responsável")
                                .Bold().FontSize(9.5f);
                            signCol.Item().Text("NeuroSync - Gestão Clínica em Neuropsicopedagogia")
                                .FontSize(7.5f).FontColor(Colors.Grey.Darken1);
                        });
                    });

                    // 3. Rodapé
                    page.Footer().Column(fCol =>
                    {
                        fCol.Item().LineHorizontal(0.5f).LineColor(Colors.Grey.Lighten1);
                        fCol.Item().PaddingTop(4).Row(r =>
                        {
                            r.RelativeItem(8).Text("NeuroSync Gestão Clínica • Registro Eletrônico de Saúde do Paciente").FontSize(7.5f).FontColor(Colors.Grey.Darken1);
                            r.RelativeItem(4).AlignRight().Text(x =>
                            {
                                x.Span("Página ").FontSize(7.5f);
                                x.CurrentPageNumber().FontSize(7.5f);
                                x.Span(" de ").FontSize(7.5f);
                                x.TotalPages().FontSize(7.5f);
                            });
                        });
                    });
                });
            });

            byte[] pdfBytes = documento.GeneratePdf();
            return File(pdfBytes, "application/pdf", $"Evolucao-{paciente.Nome.Replace(" ", "_")}-{evolucao.DataRegistro:yyyyMMdd}.pdf");
        }

        // =========================================================================
        // 4. PARECERES TÉCNICOS DE NEUROPSICOPEDAGOGIA
        // =========================================================================

        /// <summary>
        /// Cria ou atualiza o parecer técnico de avaliação neuropsicopedagógica.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SalvarParecerTecnico(ParecerTecnico model)
        {
            if (model.PacienteId <= 0) return BadRequest();

            if (string.IsNullOrWhiteSpace(model.Titulo))
                model.Titulo = "Relatório de Avaliação Neuropsicopedagógica";

            if (model.DataEmissao == default)
                model.DataEmissao = DateTime.Now;

            if (ModelState.IsValid)
            {
                if (model.IdParecer == 0)
                    context.PareceresTecnicos.Add(model);
                else
                    context.PareceresTecnicos.Update(model);

                await context.SaveChangesAsync();
                TempData["MensagemSucesso"] = "Parecer técnico salvo com sucesso!";
                return RedirectToAction(nameof(Details), new { id = model.PacienteId, aba = "parecer" });
            }

            TempData["MensagemErro"] = "Por favor, verifique os campos do parecer.";
            return RedirectToAction(nameof(Details), new { id = model.PacienteId, aba = "parecer" });
        }

        /// <summary>
        /// Emite documento formal em PDF do parecer técnico neuropsicopedagógico via QuestPDF.
        /// </summary>
        public async Task<IActionResult> GerarParecerPdf(int id)
        {
            var parecer = await context.PareceresTecnicos
                .Include(p => p.Paciente)
                .FirstOrDefaultAsync(p => p.IdParecer == id);

            if (parecer?.Paciente == null) return NotFound();

            var paciente = parecer.Paciente;
            var culturaBr = new CultureInfo("pt-BR");

            var idade = DateTime.Today.Year - paciente.DataNascimento.Year;
            if (paciente.DataNascimento.Date > DateTime.Today.AddYears(-idade)) idade--;

            string responsavel = !string.IsNullOrWhiteSpace(paciente.Responsavel) ? paciente.Responsavel :
                                !string.IsNullOrWhiteSpace(paciente.NomeMae) ? paciente.NomeMae :
                                !string.IsNullOrWhiteSpace(paciente.NomePai) ? paciente.NomePai : "Não informado";

            string logoPath = Path.Combine(hostEnvironment.WebRootPath, "images", "logo-principal-cerebro-coracao 2.png");
            byte[]? logoBytes = System.IO.File.Exists(logoPath) ? System.IO.File.ReadAllBytes(logoPath) : null;

            var documento = Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(32);
                    page.DefaultTextStyle(x => x.FontSize(9.5f).FontColor(Color.FromHex("#1e293b")));

                    // 1. Cabeçalho
                    page.Header().Column(headerCol =>
                    {
                        headerCol.Item().Row(row =>
                        {
                            row.RelativeItem(7).Row(bRow =>
                            {
                                if (logoBytes != null)
                                {
                                    bRow.ConstantItem(46).Height(46).Image(logoBytes).FitArea();
                                    bRow.ConstantItem(10);
                                }

                                bRow.RelativeItem().Column(brandCol =>
                                {
                                    brandCol.Item().Row(logoRow =>
                                    {
                                        logoRow.AutoItem().Text("Neuro").FontSize(20).Bold().FontColor(Color.FromHex("#071A3A"));
                                        logoRow.AutoItem().Text("Sync").FontSize(20).Bold().FontColor(Color.FromHex("#315BEF"));
                                    });
                                    brandCol.Item().Text("Clínica de Desenvolvimento e Neuropsicopedagogia").FontSize(8.5f).FontColor(Colors.Grey.Darken1);
                                    brandCol.Item().Text("Atendimento Especializado em Neuropsicopedagogia").FontSize(7.5f).FontColor(Colors.Grey.Medium);
                                });
                            });

                            row.RelativeItem(5).AlignRight().Column(metaCol =>
                            {
                                metaCol.Item().Text("PARECER TÉCNICO").FontSize(12).Bold().FontColor(Color.FromHex("#071A3A"));
                                metaCol.Item().Text($"Registro: #P{parecer.IdParecer:D4}").FontSize(8.5f).FontColor(Color.FromHex("#315BEF")).Bold();
                                metaCol.Item().Text($"Emissão: {parecer.DataEmissao:dd/MM/yyyy}").FontSize(8.5f).FontColor(Colors.Grey.Darken2);
                            });
                        });

                        headerCol.Item().PaddingTop(8).LineHorizontal(2).LineColor(Color.FromHex("#315BEF"));
                    });

                    // 2. Conteúdo do Laudo
                    page.Content().PaddingTop(12).Column(contentCol =>
                    {
                        contentCol.Item().Border(1).BorderColor(Color.FromHex("#e2e8f0")).Background(Color.FromHex("#f8fafc")).Padding(10).Column(pCol =>
                        {
                            pCol.Item().Text("DADOS DO PACIENTE").FontSize(8.5f).Bold().FontColor(Color.FromHex("#315BEF"));
                            pCol.Item().PaddingTop(4).Row(r =>
                            {
                                r.RelativeItem(7).Text(t => { t.Span("Nome: ").Bold(); t.Span(paciente.Nome); });
                                r.RelativeItem(3).Text(t => { t.Span("Nascimento: ").Bold(); t.Span(paciente.DataNascimento.ToString("dd/MM/yyyy")); });
                                r.RelativeItem(2).Text(t => { t.Span("Idade: ").Bold(); t.Span($"{idade} anos"); });
                            });
                            pCol.Item().PaddingTop(3).Row(r =>
                            {
                                r.RelativeItem(6).Text(t => { t.Span("Responsável: ").Bold(); t.Span(responsavel); });
                                r.RelativeItem(6).Text(t => { t.Span("Profissional: ").Bold().FontColor(Color.FromHex("#0f172a")); t.Span($"{parecer.ProfissionalNome} ({parecer.RegistroProfissional})"); });
                            });
                        });

                        contentCol.Item().PaddingTop(12).AlignCenter().Text(parecer.Titulo.ToUpper()).FontSize(11).Bold().FontColor(Color.FromHex("#071A3A"));

                        void AddSecao(string titulo, string? conteudo)
                        {
                            if (!string.IsNullOrWhiteSpace(conteudo))
                            {
                                contentCol.Item().PaddingTop(10).Text(titulo).FontSize(9.5f).Bold().FontColor(Color.FromHex("#071A3A"));
                                contentCol.Item().PaddingTop(3).Border(1).BorderColor(Color.FromHex("#E2E8F0")).Padding(8).Text(conteudo).LineHeight(1.35f);
                            }
                        }

                        AddSecao("1. MOTIVO DA AVALIAÇÃO / QUEIXA PRINCIPAL", parecer.MotivoAvaliacao);
                        AddSecao("2. PROCEDIMENTOS E RECURSOS UTILIZADOS", parecer.ProcedimentosRecursos);
                        AddSecao("3. ANÁLISE AVALIATIVA E DESEMPENHO COGNITIVO", parecer.AnaliseAvaliativa);
                        AddSecao("4. SÍNTESE AVALIATIVA / CONCLUSÃO DIAGNÓSTICA", parecer.SinteseAvaliativa);
                        AddSecao("5. RECOMENDAÇÕES E ENCAMINHAMENTOS", parecer.RecomendacoesFinais);

                        contentCol.Item().PaddingTop(26).AlignCenter().Column(sigCol =>
                        {
                            sigCol.Item().Width(240).LineHorizontal(1).LineColor(Colors.Grey.Medium);
                            sigCol.Item().PaddingTop(4).AlignCenter().Text(parecer.ProfissionalNome).Bold().FontSize(10.5f).FontColor(Color.FromHex("#071A3A"));
                            sigCol.Item().AlignCenter().Text(parecer.RegistroProfissional ?? "Neuropsicopedagoga Clínica").FontSize(9).FontColor(Colors.Grey.Darken2);
                        });
                    });

                    // 3. Rodapé
                    page.Footer().Row(r =>
                    {
                        r.RelativeItem().Text("NeuroSync • Prontuário Eletrônico e Gestão Clínica").FontSize(8).FontColor(Colors.Grey.Medium);
                        r.RelativeItem().AlignRight().Text(x =>
                        {
                            x.Span("Página ").FontSize(8);
                            x.CurrentPageNumber().FontSize(8);
                            x.Span(" de ").FontSize(8);
                            x.TotalPages().FontSize(8);
                        });
                    });
                });
            });

            var pdfBytes = documento.GeneratePdf();
            var nomeSanitizado = string.Join("_", paciente.Nome.Split(Path.GetInvalidFileNameChars())).Replace(" ", "_");
            return File(pdfBytes, "application/pdf", $"Parecer_Tecnico_{nomeSanitizado}_{parecer.IdParecer}.pdf");
        }

        // =========================================================================
        // 5. UPLOAD E GESTÃO DE ANEXOS
        // =========================================================================

        /// <summary>
        /// Salva arquivo anexo (PDF, exame, foto de atividade) vinculado ao prontuário do paciente.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UploadArquivo(int PacienteId, IFormFile arquivoUpload)
        {
            if (arquivoUpload != null && arquivoUpload.Length > 0)
            {
                string pastaUploads = Path.Combine(hostEnvironment.WebRootPath, "uploads");
                if (!Directory.Exists(pastaUploads))
                    Directory.CreateDirectory(pastaUploads);

                string nomeUnico = $"{Guid.NewGuid()}_{arquivoUpload.FileName}";
                string caminhoCompleto = Path.Combine(pastaUploads, nomeUnico);

                using (var stream = new FileStream(caminhoCompleto, FileMode.Create))
                {
                    await arquivoUpload.CopyToAsync(stream);
                }

                context.Anexos.Add(new Anexo
                {
                    PacienteId = PacienteId,
                    NomeArquivo = arquivoUpload.FileName,
                    CaminhoArquivo = $"/uploads/{nomeUnico}",
                    DataUpload = DateTime.Now
                });

                await context.SaveChangesAsync();
            }

            return RedirectToAction(nameof(Details), new { id = PacienteId, aba = "anexos" });
        }

        // =========================================================================
        // 6. CADASTRO, EDIÇÃO E EXCLUSÃO DE PACIENTES
        // =========================================================================

        [HttpGet]
        public IActionResult Create() => View();

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(Paciente paciente)
        {
            if (ModelState.IsValid)
            {
                context.Add(paciente);
                await context.SaveChangesAsync();
                return RedirectToAction(nameof(Index));
            }
            return View(paciente);
        }

        [HttpGet]
        public async Task<IActionResult> Edit(int? id, string aba = "resumo")
        {
            if (id == null) return NotFound();
            var paciente = await context.Pacientes.FindAsync(id.Value);
            if (paciente == null) return NotFound();
            ViewBag.AbaAtiva = string.IsNullOrEmpty(aba) ? "resumo" : aba.ToLower();
            return View(paciente);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, Paciente paciente, string abaAtiva = "resumo")
        {
            if (id != paciente.IdPaciente) return NotFound();

            if (ModelState.IsValid)
            {
                context.Update(paciente);
                await context.SaveChangesAsync();
                return RedirectToAction(nameof(Details), new { id = paciente.IdPaciente, aba = !string.IsNullOrEmpty(abaAtiva) ? abaAtiva : "resumo" });
            }
            ViewBag.AbaAtiva = abaAtiva;
            return View(paciente);
        }

        [HttpGet]
        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null) return NotFound();
            var paciente = await context.Pacientes.FindAsync(id.Value);
            return paciente == null ? NotFound() : View(paciente);
        }

        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var paciente = await context.Pacientes.FindAsync(id);
            if (paciente != null)
            {
                context.Pacientes.Remove(paciente);
                await context.SaveChangesAsync();
            }
            return RedirectToAction(nameof(Index));
        }
    }