using AiDetect.Core.Features;

namespace AiDetect.Tests;

public class FeatureExtractorTests
{
    private const string AiStyle = """
        namespace Shop.Services;

        /// <summary>
        /// Provides order operations.
        /// </summary>
        public sealed class OrderService : IOrderService
        {
            private readonly ShopDbContext _dbContext;
            private readonly ILogger<OrderService> _logger;

            public OrderService(ShopDbContext dbContext, ILogger<OrderService> logger)
            {
                _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
                _logger = logger;
            }

            /// <summary>
            /// Retrieves an order by its identifier.
            /// </summary>
            /// <param name="orderId">The unique identifier of the order.</param>
            /// <param name="cancellationToken">A token to cancel the operation.</param>
            /// <returns>The order if found; otherwise, <c>null</c>.</returns>
            public async Task<OrderDto?> GetOrderByIdAsync(Guid orderId, CancellationToken cancellationToken = default)
            {
                var order = await _dbContext.Orders
                    .AsNoTracking()
                    .FirstOrDefaultAsync(o => o.Id == orderId, cancellationToken);

                if (order is null)
                {
                    _logger.LogWarning("Order {OrderId} was not found", orderId);
                    return null;
                }

                return order.ToDto();
            }
        }
        """;

    private const string HumanStyle = """
        namespace Shop.Services
        {
            public class OrderService
            {
                private ShopDbContext _db;

                public async Task<OrderDto> GetOrder(Guid id)
                {
                    var o = await _db.Orders.Include(x => x.Items).FirstOrDefaultAsync(x => x.Id == id);
                    if (o == null) throw new NotFoundException($"order {id} not found"); // TODO: в middleware
                    _logger.LogInformation($"loaded order {id}");
                    //var dto = Map(o);
                    return _mapper.Map<OrderDto>(o);
                }
            }
        }
        """;

    private static CodeStats Extract(string code, bool isTest = false) =>
        CSharpFeatureExtractor.Extract("Test.cs", code, _ => true, isTest, isNewFile: true);

    [Fact]
    public void AiStyle_HasDocsTokensTemplatesAndPatterns()
    {
        var s = Extract(AiStyle);

        Assert.Equal(1, s[K.FileScopedNs]);
        Assert.Equal(1, s[K.SealedClasses]);
        Assert.Equal(1, s[K.AsyncWithCt]);
        Assert.Equal(1, s[K.AsyncSuffix]);
        Assert.Equal(1, s[K.CtDefault]);
        Assert.Equal(1, s[K.AwaitsPassingCt]);
        Assert.Equal(1, s[K.LogTemplated]);
        Assert.Equal(1, s[K.NullPatternChecks]);
        Assert.Equal(1, s[K.DocMethodsComplete]);
        Assert.True(s[K.DocTemplatePhrase] >= 1);
        Assert.Equal(1, s[K.GuardCoalesceThrow]);
        Assert.Equal(0, s[K.TodoComments]);
    }

    [Fact]
    public void HumanStyle_HasTodoInterpolationShortNamesAndCommentedCode()
    {
        var s = Extract(HumanStyle);

        Assert.Equal(1, s[K.BlockNs]);
        Assert.Equal(1, s[K.TodoComments]);
        Assert.Equal(1, s[K.CommentedOutCode]);
        Assert.Equal(1, s[K.LogInterpolated]);
        Assert.Equal(1, s[K.NullEqualityChecks]);
        Assert.Equal(0, s[K.AsyncWithCt]);
        Assert.Equal(1, s[K.ThrowMessageLowercase]);
        Assert.Contains("o", s.LocalNames);
        Assert.Equal(0, s[K.SealedClasses]);
    }

    [Fact]
    public void OnlyLinesInScopeAreCounted()
    {
        var s = CSharpFeatureExtractor.Extract("Test.cs", HumanStyle, line => line == 10, false, false);

        Assert.Equal(1, s[K.TodoComments]);
        Assert.Equal(0, s[K.LogInterpolated]);
    }

    [Fact]
    public void ArrangeActAssertMarkers_AreTestFeatures_NotProseComments()
    {
        const string test = """
            public class OrderTests
            {
                [Fact]
                public void Create_WithValidData_ReturnsOrder()
                {
                    // Arrange
                    var sut = new Order();
                    // Act
                    var result = sut.Create("test@example.com");
                    // Assert
                    Assert.NotNull(result);
                }
            }
            """;

        var s = Extract(test, isTest: true);

        Assert.Equal(1, s[K.TestMethods]);
        Assert.Equal(1, s[K.TestAaa]);
        Assert.Equal(1, s[K.TestNamePattern]);
        Assert.Equal(1, s[K.TestExampleData]);
        Assert.Equal(0, s[K.LineComments]);
    }

    [Theory]
    [InlineData("var x = Foo();", true)]
    [InlineData("_logger.LogInformation(\"x\");", true)]
    [InlineData("if (order.Status == \"shipped\") return;", true)]
    [InlineData("Validate the request before saving", false)]
    [InlineData("цены храним с 2 знаками", false)]
    public void LooksLikeCode_DetectsCommentedOutCode(string body, bool expected) =>
        Assert.Equal(expected, CSharpFeatureExtractor.LooksLikeCode(body));

    [Fact]
    public void FormattingIrregularities_AreDetected()
    {
        const string sloppy = "class A\n{\n    void M()\n    {\n        if(x==1){ y=2; }   \n        Foo(a,b);\n    }\n}\n";

        var s = Extract(sloppy);

        Assert.True(s[K.KeywordNoSpace] >= 1);
        Assert.True(s[K.TrailingWhitespace] >= 1);
        Assert.True(s[K.CommaNoSpace] >= 1);
        Assert.True(s[K.OperatorNoSpace] >= 1);
    }

    [Fact]
    public void Typos_AreDetectedInIdentifiersAndComments()
    {
        const string code = "class A\n{\n    // recieve the responce\n    int GetAdressCount() => 1;\n}\n";

        var s = Extract(code);

        Assert.True(s[K.Typos] >= 2);
    }
}
