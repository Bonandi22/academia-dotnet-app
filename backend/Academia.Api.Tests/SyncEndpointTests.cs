using System.Net;
using System.Net.Http.Json;
using Academia.Api.Learning;
using Academia.Application.Contracts;

namespace Academia.Api.Tests;

[Collection(ApiCollection.Name)]
public sealed class SyncEndpointTests(ApiFactory api)
{
    private static ReviewRequest Review(string itemKey, bool resolved = false) =>
        new(itemKey, "Pergunta de teste", "csharp", "C#", "quiz", "Resposta errada.", resolved);

    private static SyncRequest FullSync(Guid attemptId) => new(
        Lessons: [new("csharp", "async-await", true), new("csharp", "di-lifetimes", false)],
        Reviews: [Review("quiz:q1"), Review("lesson:csharp:async-await")],
        QuizAttempts: [new(attemptId, 4, 5, ["q1"], DateTimeOffset.UtcNow.AddHours(-1))],
        Flashcards: [new("auth-vs-authz", "again")]);

    private static async Task<HttpResponseMessage> SyncAsync(HttpClient client, SyncRequest request) =>
        await client.PostAsJsonAsync("/api/learning/sync", request);

    [Fact]
    public async Task Sync_exige_token()
    {
        var response = await SyncAsync(api.CreateClient(), new SyncRequest());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Sync_aplica_tudo_e_devolve_o_snapshot_num_so_pedido()
    {
        var (client, _) = await api.CreateUserAsync();

        var response = await SyncAsync(client, FullSync(Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var snapshot = (await response.Content.ReadFromJsonAsync<LearningSnapshotResponse>())!;
        Assert.Equal(2, snapshot.Lessons.Count);
        Assert.False(snapshot.Lessons.Single(l => l.LessonId == "di-lifetimes").IsCompleted);
        Assert.Equal(2, snapshot.Reviews.Count);
        Assert.Single(snapshot.QuizAttempts);
        Assert.Equal("again", Assert.Single(snapshot.Flashcards).Rating);
    }

    [Fact]
    public async Task Repetir_o_mesmo_sync_nao_duplica_nada()
    {
        var (client, _) = await api.CreateUserAsync();
        var request = FullSync(Guid.NewGuid());

        await SyncAsync(client, request);
        var second = await (await SyncAsync(client, request)).Content.ReadFromJsonAsync<LearningSnapshotResponse>();

        Assert.Equal(2, second!.Lessons.Count);
        Assert.Equal(2, second.Reviews.Count);
        Assert.Single(second.QuizAttempts);
        Assert.Single(second.Flashcards);
    }

    [Fact]
    public async Task Um_item_invalido_rejeita_o_sync_inteiro_sem_gravar_nada()
    {
        var (client, _) = await api.CreateUserAsync();
        var request = FullSync(Guid.NewGuid()) with
        {
            Reviews = [Review("quiz:valida"), Review("quiz:invalida") with { ModuleId = new string('m', 81) }]
        };

        var response = await SyncAsync(client, request);
        var snapshot = await client.GetFromJsonAsync<LearningSnapshotResponse>("/api/learning/snapshot");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("reviews[1]", await response.Content.ReadAsStringAsync());
        Assert.Empty(snapshot!.Lessons);
        Assert.Empty(snapshot.Reviews);
        Assert.Empty(snapshot.QuizAttempts);
        Assert.Empty(snapshot.Flashcards);
    }

    [Fact]
    public async Task Listas_acima_do_limite_sao_rejeitadas()
    {
        var (client, _) = await api.CreateUserAsync();
        var attempts = Enumerable.Range(0, LearningStore.MaxQuizAttemptsPerSync + 1)
            .Select(_ => new QuizAttemptRequest(Guid.NewGuid(), 1, 1, [])).ToList();

        var response = await SyncAsync(client, new SyncRequest(QuizAttempts: attempts));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("quizAttempts", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Revisoes_apagadas_no_sync_so_afetam_a_propria_conta()
    {
        var (alice, _) = await api.CreateUserAsync();
        var (bob, _) = await api.CreateUserAsync();
        await SyncAsync(alice, new SyncRequest(Reviews: [Review("quiz:partilhada")]));
        await SyncAsync(bob, new SyncRequest(Reviews: [Review("quiz:partilhada")]));

        var bobAfter = await (await SyncAsync(bob, new SyncRequest(DeletedReviewKeys: ["quiz:partilhada"]))).Content.ReadFromJsonAsync<LearningSnapshotResponse>();
        var aliceAfter = await alice.GetFromJsonAsync<LearningSnapshotResponse>("/api/learning/snapshot");

        Assert.Empty(bobAfter!.Reviews);
        Assert.Single(aliceAfter!.Reviews);
    }

    [Fact]
    public async Task Apagar_ganha_a_atualizar_a_mesma_revisao_no_mesmo_sync()
    {
        var (client, _) = await api.CreateUserAsync();
        await SyncAsync(client, new SyncRequest(Reviews: [Review("quiz:q9")]));

        var after = await (await SyncAsync(client, new SyncRequest(DeletedReviewKeys: ["quiz:q9"], Reviews: [Review("quiz:q9", resolved: true)])))
            .Content.ReadFromJsonAsync<LearningSnapshotResponse>();

        Assert.Empty(after!.Reviews);
    }

    [Fact]
    public async Task Chaves_que_so_diferem_em_maiusculas_sao_a_mesma_linha()
    {
        var (client, _) = await api.CreateUserAsync();

        var response = await SyncAsync(client, new SyncRequest(
            Reviews: [Review("quiz:Q1"), Review("QUIZ:q1", resolved: true)],
            Lessons: [new("csharp", "async-await", true), new("CSharp", "Async-Await", false)],
            Flashcards: [new("auth-vs-authz", "again"), new("AUTH-VS-AUTHZ", "easy")]));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var snapshot = (await response.Content.ReadFromJsonAsync<LearningSnapshotResponse>())!;
        Assert.True(Assert.Single(snapshot.Reviews).IsResolved);
        Assert.False(Assert.Single(snapshot.Lessons).IsCompleted);
        Assert.Equal("easy", Assert.Single(snapshot.Flashcards).Rating);
    }

    [Fact]
    public async Task Sync_vazio_devolve_o_snapshot_atual()
    {
        var (client, _) = await api.CreateUserAsync();
        await SyncAsync(client, new SyncRequest(Flashcards: [new("auth-vs-authz", "good")]));

        var response = await client.PostAsync("/api/learning/sync", JsonContent.Create(new { }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Single((await response.Content.ReadFromJsonAsync<LearningSnapshotResponse>())!.Flashcards);
    }
}
