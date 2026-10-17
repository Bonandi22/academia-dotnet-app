using System.Net;
using System.Net.Http.Json;
using Academia.Application.Contracts;

namespace Academia.Api.Tests;

[Collection(ApiCollection.Name)]
public sealed class LearningEndpointsTests(ApiFactory api)
{
    private static ReviewRequest Review(string itemKey = "quiz:q1", string moduleId = "csharp", string? moduleTitle = "C#") =>
        new(itemKey, "Pergunta de teste", moduleId, moduleTitle!, "quiz", "Resposta errada.");

    [Theory]
    [InlineData("GET", "/api/learning/snapshot")]
    [InlineData("PUT", "/api/learning/lessons")]
    [InlineData("PUT", "/api/learning/reviews")]
    [InlineData("DELETE", "/api/learning/reviews/quiz:q1")]
    [InlineData("POST", "/api/learning/quiz-attempts")]
    [InlineData("PUT", "/api/learning/flashcards")]
    public async Task Endpoints_de_aprendizagem_exigem_token(string method, string url)
    {
        var request = new HttpRequestMessage(new HttpMethod(method), url) { Content = JsonContent.Create(new { }) };

        var response = await api.CreateClient().SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Dados_de_um_utilizador_nao_sao_visiveis_nem_alteraveis_por_outro()
    {
        var (alice, _) = await api.CreateUserAsync();
        var (bob, _) = await api.CreateUserAsync();
        (await alice.PutAsJsonAsync("/api/learning/lessons", new LessonProgressRequest("csharp", "0", true))).EnsureSuccessStatusCode();
        (await alice.PutAsJsonAsync("/api/learning/reviews", Review())).EnsureSuccessStatusCode();
        (await alice.PostAsJsonAsync("/api/learning/quiz-attempts", new QuizAttemptRequest(Guid.NewGuid(), 3, 5, ["q1", "q2"]))).EnsureSuccessStatusCode();
        (await alice.PutAsJsonAsync("/api/learning/flashcards", new FlashcardRatingRequest("auth-vs-authz", "again"))).EnsureSuccessStatusCode();

        var bobSnapshot = await bob.GetFromJsonAsync<LearningSnapshotResponse>("/api/learning/snapshot");
        var bobDelete = await bob.DeleteAsync("/api/learning/reviews/quiz:q1");
        var aliceSnapshot = await alice.GetFromJsonAsync<LearningSnapshotResponse>("/api/learning/snapshot");

        Assert.Empty(bobSnapshot!.Lessons);
        Assert.Empty(bobSnapshot.Reviews);
        Assert.Empty(bobSnapshot.QuizAttempts);
        Assert.Empty(bobSnapshot.Flashcards);
        Assert.Equal(HttpStatusCode.NotFound, bobDelete.StatusCode);
        Assert.Single(aliceSnapshot!.Reviews);
        Assert.Single(aliceSnapshot.Lessons);
        Assert.Single(aliceSnapshot.QuizAttempts);
        Assert.Single(aliceSnapshot.Flashcards);
    }

    [Fact]
    public async Task Aula_desmarcada_atualiza_a_mesma_linha_em_vez_de_criar_outra()
    {
        var (client, _) = await api.CreateUserAsync();

        (await client.PutAsJsonAsync("/api/learning/lessons", new LessonProgressRequest("csharp", "0", true))).EnsureSuccessStatusCode();
        (await client.PutAsJsonAsync("/api/learning/lessons", new LessonProgressRequest("csharp", "0", false))).EnsureSuccessStatusCode();
        var snapshot = await client.GetFromJsonAsync<LearningSnapshotResponse>("/api/learning/snapshot");

        var lesson = Assert.Single(snapshot!.Lessons);
        Assert.False(lesson.IsCompleted);
    }

    [Theory]
    [InlineData("quiz:a", 81, 1)]   // ModuleId acima de HasMaxLength(80)
    [InlineData("quiz:b", 0, 1)]    // ModuleId vazio
    [InlineData("quiz:c", 6, 181)]  // ModuleTitle acima de HasMaxLength(180)
    public async Task Revisao_fora_dos_limites_da_base_devolve_400(string itemKey, int moduleIdLength, int moduleTitleLength)
    {
        var (client, _) = await api.CreateUserAsync();

        var response = await client.PutAsJsonAsync("/api/learning/reviews",
            Review(itemKey, new string('m', moduleIdLength), new string('t', moduleTitleLength)));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Revisao_no_limite_e_sem_titulo_de_modulo_e_aceite()
    {
        var (client, _) = await api.CreateUserAsync();

        var atLimit = await client.PutAsJsonAsync("/api/learning/reviews", Review("quiz:limite", new string('m', 80)));
        var withoutTitle = await client.PutAsJsonAsync("/api/learning/reviews", Review("quiz:sem-titulo", moduleTitle: null));
        var snapshot = await client.GetFromJsonAsync<LearningSnapshotResponse>("/api/learning/snapshot");

        Assert.Equal(HttpStatusCode.NoContent, atLimit.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, withoutTitle.StatusCode);
        Assert.Equal("", snapshot!.Reviews.Single(r => r.ItemKey == "quiz:sem-titulo").ModuleTitle);
    }

    [Theory]
    [InlineData(6, 5)]   // score acima do total
    [InlineData(0, 0)]   // total abaixo de 1
    [InlineData(-1, 5)]  // score negativo
    public async Task Tentativa_de_quiz_incoerente_devolve_400(int score, int total)
    {
        var (client, _) = await api.CreateUserAsync();

        var response = await client.PostAsJsonAsync("/api/learning/quiz-attempts", new QuizAttemptRequest(Guid.NewGuid(), score, total, []));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Tentativa_de_quiz_sem_id_do_cliente_devolve_400()
    {
        var (client, _) = await api.CreateUserAsync();

        var response = await client.PostAsJsonAsync("/api/learning/quiz-attempts", new QuizAttemptRequest(Guid.Empty, 3, 5, []));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Reenviar_a_mesma_tentativa_de_quiz_nao_a_duplica()
    {
        var (client, _) = await api.CreateUserAsync();
        var attempt = new QuizAttemptRequest(Guid.NewGuid(), 4, 5, ["q3"], DateTimeOffset.UtcNow.AddDays(-2));

        var first = await client.PostAsJsonAsync("/api/learning/quiz-attempts", attempt);
        var retry = await client.PostAsJsonAsync("/api/learning/quiz-attempts", attempt with { Score = 5 });
        var snapshot = await client.GetFromJsonAsync<LearningSnapshotResponse>("/api/learning/snapshot");

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        var stored = Assert.Single(snapshot!.QuizAttempts);
        Assert.Equal(attempt.Id, stored.Id);
        Assert.Equal(4, stored.Score); // o reenvio não reescreve a tentativa original
        Assert.True((attempt.CompletedAt!.Value - stored.CompletedAt).Duration() < TimeSpan.FromMilliseconds(1), "A data enviada pelo cliente devia ser preservada.");
    }

    [Fact]
    public async Task Envios_simultaneos_da_mesma_tentativa_gravam_uma_so_linha()
    {
        var (client, _) = await api.CreateUserAsync();
        var attempt = new QuizAttemptRequest(Guid.NewGuid(), 2, 5, ["q1"]);

        var responses = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => client.PostAsJsonAsync("/api/learning/quiz-attempts", attempt)));
        var snapshot = await client.GetFromJsonAsync<LearningSnapshotResponse>("/api/learning/snapshot");

        Assert.All(responses, r => Assert.True(r.IsSuccessStatusCode, $"HTTP {(int)r.StatusCode}"));
        Assert.Single(snapshot!.QuizAttempts);
    }

    [Fact]
    public async Task Mesmo_id_de_tentativa_em_contas_diferentes_nao_colide()
    {
        var (alice, _) = await api.CreateUserAsync();
        var (bob, _) = await api.CreateUserAsync();
        var attempt = new QuizAttemptRequest(Guid.NewGuid(), 3, 5, []);

        var fromAlice = await alice.PostAsJsonAsync("/api/learning/quiz-attempts", attempt);
        var fromBob = await bob.PostAsJsonAsync("/api/learning/quiz-attempts", attempt);

        Assert.Equal(HttpStatusCode.Created, fromAlice.StatusCode);
        Assert.Equal(HttpStatusCode.Created, fromBob.StatusCode);
    }

    [Fact]
    public async Task Data_da_tentativa_no_futuro_passa_a_agora()
    {
        var (client, _) = await api.CreateUserAsync();
        var before = DateTimeOffset.UtcNow;

        await client.PostAsJsonAsync("/api/learning/quiz-attempts", new QuizAttemptRequest(Guid.NewGuid(), 3, 5, [], DateTimeOffset.UtcNow.AddYears(1)));
        var snapshot = await client.GetFromJsonAsync<LearningSnapshotResponse>("/api/learning/snapshot");

        Assert.InRange(Assert.Single(snapshot!.QuizAttempts).CompletedAt, before, DateTimeOffset.UtcNow);
    }
}
