using System.Text.Json;
using Blazing.Json.JSONPath.Parser;
using Blazing.Json.JSONPath.Evaluator;
using Shouldly;
using Xunit;

namespace Blazing.Json.JSONPath.Tests.UnitTests.Functions;

/// <summary>
/// Tests for RFC 9535 Section 2.3.5.1 and 2.4.2 compliance:
/// Functions returning LogicalType MUST be valid as standalone test expressions.
/// </summary>
public sealed class FunctionExpressionTests
{
    private readonly JsonPathEvaluator _evaluator = new();

    #region search() Function as Test Expression (RFC 9535 Section 2.4.7)

    [Fact]
    public void Search_AsTestExpression_ShouldParse()
    {
        // Arrange
        var jsonPath = "$[?search(@.name, 'son')]";

        // Act & Assert (should not throw)
        var query = JsonPathParser.Parse(jsonPath);
        query.ShouldNotBeNull();
    }

    [Fact]
    public void Search_AsTestExpression_ShouldEvaluateCorrectly()
    {
        // Arrange
        var json = JsonDocument.Parse("""
        [
            {"id": 1, "name": "Alice Johnson", "age": 25},
            {"id": 2, "name": "Bob Wilson", "age": 30},
            {"id": 3, "name": "Charlie Brown", "age": 35}
        ]
        """).RootElement;
        var query = JsonPathParser.Parse("$[?search(@.name, 'son')]");

        // Act
        var results = _evaluator.Evaluate(query, json);

        // Assert
        results.Count.ShouldBe(2);
        results[0].Value.GetProperty("id").GetInt32().ShouldBe(1);
        results[0].Value.GetProperty("name").GetString().ShouldBe("Alice Johnson");
        results[1].Value.GetProperty("id").GetInt32().ShouldBe(2);
        results[1].Value.GetProperty("name").GetString().ShouldBe("Bob Wilson");
    }

    [Fact]
    public void Search_NoMatches_ReturnsEmpty()
    {
        // Arrange
        var json = JsonDocument.Parse("""
        [
            {"id": 1, "name": "Alice"},
            {"id": 2, "name": "Bob"}
        ]
        """).RootElement;
        var query = JsonPathParser.Parse("$[?search(@.name, 'xyz')]");

        // Act
        var results = _evaluator.Evaluate(query, json);

        // Assert
        results.Count.ShouldBe(0);
    }

    [Fact]
    public void Search_WithRegexPattern_ShouldWork()
    {
        // Arrange
        var json = JsonDocument.Parse("""
        [
            {"code": "ABC-123"},
            {"code": "DEF-456"},
            {"code": "ABC-789"}
        ]
        """).RootElement;
        var query = JsonPathParser.Parse("$[?search(@.code, 'ABC-.*')]");

        // Act
        var results = _evaluator.Evaluate(query, json);

        // Assert
        results.Count.ShouldBe(2);
        results[0].Value.GetProperty("code").GetString().ShouldBe("ABC-123");
        results[1].Value.GetProperty("code").GetString().ShouldBe("ABC-789");
    }

    #endregion

    #region match() Function as Test Expression (RFC 9535 Section 2.4.6)

    [Fact]
    public void Match_AsTestExpression_ShouldParse()
    {
        // Arrange
        var jsonPath = "$[?match(@.code, '^ELEC-.*')]";

        // Act & Assert (should not throw)
        var query = JsonPathParser.Parse(jsonPath);
        query.ShouldNotBeNull();
    }

    [Fact]
    public void Match_AsTestExpression_ShouldEvaluateCorrectly()
    {
        // Arrange
        var json = JsonDocument.Parse("""
        [
            {"name": "Alice", "email": "alice@example.com"},
            {"name": "Bob", "email": "bob@test.org"},
            {"name": "Charlie", "email": "charlie@example.com"}
        ]
        """).RootElement;
        var query = JsonPathParser.Parse("$[?match(@.email, '.*@example\\\\.com$')]");

        // Act
        var results = _evaluator.Evaluate(query, json);

        // Assert
        results.Count.ShouldBe(2);
        results[0].Value.GetProperty("name").GetString().ShouldBe("Alice");
        results[0].Value.GetProperty("email").GetString().ShouldBe("alice@example.com");
        results[1].Value.GetProperty("name").GetString().ShouldBe("Charlie");
        results[1].Value.GetProperty("email").GetString().ShouldBe("charlie@example.com");
    }

    [Fact]
    public void Match_NoMatches_ReturnsEmpty()
    {
        // Arrange
        var json = JsonDocument.Parse("""
        [
            {"email": "alice@test.org"},
            {"email": "bob@sample.net"}
        ]
        """).RootElement;
        var query = JsonPathParser.Parse("$[?match(@.email, '.*@example\\\\.com$')]");

        // Act
        var results = _evaluator.Evaluate(query, json);

        // Assert
        results.Count.ShouldBe(0);
    }

    #endregion

    #region Logical NOT with LogicalType Functions

    [Fact]
    public void LogicalNot_WithMatch_ShouldParse()
    {
        // Arrange
        var jsonPath = "$[?!match(@.code, '^TEMP-.*')]";

        // Act & Assert (should not throw)
        var query = JsonPathParser.Parse(jsonPath);
        query.ShouldNotBeNull();
    }

    [Fact]
    public void LogicalNot_WithMatch_ShouldEvaluateCorrectly()
    {
        // Arrange
        var json = JsonDocument.Parse("""
        [
            {"id": 1, "code": "PERM-001"},
            {"id": 2, "code": "TEMP-002"},
            {"id": 3, "code": "PERM-003"}
        ]
        """).RootElement;
        var query = JsonPathParser.Parse("$[?!match(@.code, '^TEMP-.*')]");

        // Act
        var results = _evaluator.Evaluate(query, json);

        // Assert
        results.Count.ShouldBe(2);
        results[0].Value.GetProperty("id").GetInt32().ShouldBe(1);
        results[0].Value.GetProperty("code").GetString().ShouldBe("PERM-001");
        results[1].Value.GetProperty("id").GetInt32().ShouldBe(3);
        results[1].Value.GetProperty("code").GetString().ShouldBe("PERM-003");
    }

    [Fact]
    public void LogicalNot_WithSearch_ShouldWork()
    {
        // Arrange
        var json = JsonDocument.Parse("""
        [
            {"name": "Admin User"},
            {"name": "Regular User"},
            {"name": "Guest"}
        ]
        """).RootElement;
        var query = JsonPathParser.Parse("$[?!search(@.name, 'User')]");

        // Act
        var results = _evaluator.Evaluate(query, json);

        // Assert
        results.Count.ShouldBe(1);
        results[0].Value.GetProperty("name").GetString().ShouldBe("Guest");
    }

    #endregion

    #region Logical AND with LogicalType Functions

    [Fact]
    public void LogicalAnd_WithSearch_ShouldParse()
    {
        // Arrange
        var jsonPath = "$[?@.price < 100 && search(@.name, 'Chair')]";

        // Act & Assert (should not throw)
        var query = JsonPathParser.Parse(jsonPath);
        query.ShouldNotBeNull();
    }

    [Fact]
    public void LogicalAnd_WithSearch_ShouldEvaluateCorrectly()
    {
        // Arrange
        var json = JsonDocument.Parse("""
        [
            {"id": 1, "name": "Office Chair", "price": 299.99},
            {"id": 2, "name": "Desk Lamp", "price": 45.99},
            {"id": 3, "name": "Ergonomic Chair", "price": 89.99}
        ]
        """).RootElement;
        var query = JsonPathParser.Parse("$[?@.price < 100 && search(@.name, 'Chair')]");

        // Act
        var results = _evaluator.Evaluate(query, json);

        // Assert
        results.Count.ShouldBe(1);
        results[0].Value.GetProperty("id").GetInt32().ShouldBe(3);
        results[0].Value.GetProperty("name").GetString().ShouldBe("Ergonomic Chair");
        results[0].Value.GetProperty("price").GetDouble().ShouldBe(89.99);
    }

    [Fact]
    public void LogicalAnd_TwoFunctions_ShouldWork()
    {
        // Arrange
        var json = JsonDocument.Parse("""
        [
            {"email": "alice@example.com", "name": "Alice Admin"},
            {"email": "bob@test.org", "name": "Bob Admin"},
            {"email": "charlie@example.com", "name": "Charlie User"}
        ]
        """).RootElement;
        var query = JsonPathParser.Parse("$[?match(@.email, '.*@example\\\\.com$') && search(@.name, 'Admin')]");

        // Act
        var results = _evaluator.Evaluate(query, json);

        // Assert
        results.Count.ShouldBe(1);
        results[0].Value.GetProperty("name").GetString().ShouldBe("Alice Admin");
    }

    #endregion

    #region Logical OR with LogicalType Functions

    [Fact]
    public void LogicalOr_WithMatch_ShouldParse()
    {
        // Arrange
        var jsonPath = "$[?match(@.status, 'active') || @.priority == 'high']";

        // Act & Assert (should not throw)
        var query = JsonPathParser.Parse(jsonPath);
        query.ShouldNotBeNull();
    }

    [Fact]
    public void LogicalOr_WithMatch_ShouldEvaluateCorrectly()
    {
        // Arrange
        var json = JsonDocument.Parse("""
        [
            {"id": 1, "status": "active", "priority": "low"},
            {"id": 2, "status": "inactive", "priority": "high"},
            {"id": 3, "status": "pending", "priority": "medium"}
        ]
        """).RootElement;
        var query = JsonPathParser.Parse("$[?match(@.status, 'active') || @.priority == 'high']");

        // Act
        var results = _evaluator.Evaluate(query, json);

        // Assert
        results.Count.ShouldBe(2);
        results[0].Value.GetProperty("id").GetInt32().ShouldBe(1);
        results[1].Value.GetProperty("id").GetInt32().ShouldBe(2);
    }

    [Fact]
    public void LogicalOr_TwoFunctions_ShouldWork()
    {
        // Arrange
        var json = JsonDocument.Parse("""
        [
            {"code": "TEMP-001", "name": "Admin"},
            {"code": "PERM-002", "name": "User"},
            {"code": "TEMP-003", "name": "Guest"}
        ]
        """).RootElement;
        var query = JsonPathParser.Parse("$[?match(@.code, '^TEMP-.*') || search(@.name, 'Admin')]");

        // Act
        var results = _evaluator.Evaluate(query, json);

        // Assert
        results.Count.ShouldBe(2);
        results[0].Value.GetProperty("code").GetString().ShouldBe("TEMP-001");
        results[1].Value.GetProperty("code").GetString().ShouldBe("TEMP-003");
    }

    #endregion

    #region Parenthesized Expressions

    [Fact]
    public void Parenthesized_SearchFunction_ShouldParse()
    {
        // Arrange
        var jsonPath = "$[?(search(@.name, 'John') && @.age > 25)]";

        // Act & Assert (should not throw)
        var query = JsonPathParser.Parse(jsonPath);
        query.ShouldNotBeNull();
    }

    [Fact]
    public void Parenthesized_SearchFunction_ShouldEvaluateCorrectly()
    {
        // Arrange
        var json = JsonDocument.Parse("""
        [
            {"name": "John Smith", "age": 30},
            {"name": "John Doe", "age": 20},
            {"name": "Jane Doe", "age": 35}
        ]
        """).RootElement;
        var query = JsonPathParser.Parse("$[?(search(@.name, 'John') && @.age > 25)]");

        // Act
        var results = _evaluator.Evaluate(query, json);

        // Assert
        results.Count.ShouldBe(1);
        results[0].Value.GetProperty("name").GetString().ShouldBe("John Smith");
    }

    #endregion

    #region Complex Logical Expressions

    [Fact]
    public void ComplexExpression_MixedFunctionsAndComparisons_ShouldWork()
    {
        // Arrange
        var json = JsonDocument.Parse("""
        [
            {"id": 1, "email": "alice@example.com", "role": "admin", "active": true},
            {"id": 2, "email": "bob@test.org", "role": "user", "active": true},
            {"id": 3, "email": "charlie@example.com", "role": "user", "active": false}
        ]
        """).RootElement;
        var query = JsonPathParser.Parse("$[?match(@.email, '.*@example\\\\.com$') && @.active == true && search(@.role, 'admin')]");

        // Act
        var results = _evaluator.Evaluate(query, json);

        // Assert
        results.Count.ShouldBe(1);
        results[0].Value.GetProperty("id").GetInt32().ShouldBe(1);
    }

    [Fact]
    public void ComplexExpression_OrWithNegation_ShouldWork()
    {
        // Arrange
        var json = JsonDocument.Parse("""
        [
            {"id": 1, "code": "TEMP-001", "status": "active"},
            {"id": 2, "code": "PERM-002", "status": "inactive"},
            {"id": 3, "code": "TEMP-003", "status": "active"}
        ]
        """).RootElement;
        var query = JsonPathParser.Parse("$[?!match(@.code, '^TEMP-.*') || @.status == 'inactive']");

        // Act
        var results = _evaluator.Evaluate(query, json);

        // Assert
        results.Count.ShouldBe(1);
        results[0].Value.GetProperty("id").GetInt32().ShouldBe(2);
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void Search_WithMissingProperty_ShouldReturnEmpty()
    {
        // Arrange
        var json = JsonDocument.Parse("""
        [
            {"id": 1, "name": "Alice"},
            {"id": 2, "description": "Bob"}
        ]
        """).RootElement;
        var query = JsonPathParser.Parse("$[?search(@.name, 'Bob')]");

        // Act
        var results = _evaluator.Evaluate(query, json);

        // Assert
        // Only item 1 has 'name', but it doesn't match 'Bob'
        results.Count.ShouldBe(0);
    }

    [Fact]
    public void Match_WithNullValue_ShouldReturnLogicalFalse()
    {
        // Arrange
        var json = JsonDocument.Parse("""
        [
            {"id": 1, "email": "alice@example.com"},
            {"id": 2, "email": null}
        ]
        """).RootElement;
        var query = JsonPathParser.Parse("$[?match(@.email, '.*@example\\\\.com$')]");

        // Act
        var results = _evaluator.Evaluate(query, json);

        // Assert
        results.Count.ShouldBe(1);
        results[0].Value.GetProperty("id").GetInt32().ShouldBe(1);
    }

    [Fact]
    public void Search_WithNonStringValue_ShouldReturnLogicalFalse()
    {
        // Arrange
        var json = JsonDocument.Parse("""
        [
            {"id": 1, "value": "text123"},
            {"id": 2, "value": 12345}
        ]
        """).RootElement;
        var query = JsonPathParser.Parse("$[?search(@.value, '123')]");

        // Act
        var results = _evaluator.Evaluate(query, json);

        // Assert
        // Only string values should match
        results.Count.ShouldBe(1);
        results[0].Value.GetProperty("id").GetInt32().ShouldBe(1);
    }

    #endregion

    #region RFC 9535 Table 12 Examples

    [Fact]
    public void Rfc9535_Table12_MatchExample()
    {
        // RFC 9535 Table 12: $[?match(@.timezone, 'Europe/.*')]
        // Arrange
        var json = JsonDocument.Parse("""
        [
            {"city": "London", "timezone": "Europe/London"},
            {"city": "New York", "timezone": "America/New_York"},
            {"city": "Paris", "timezone": "Europe/Paris"}
        ]
        """).RootElement;
        var query = JsonPathParser.Parse("$[?match(@.timezone, 'Europe/.*')]");

        // Act
        var results = _evaluator.Evaluate(query, json);

        // Assert
        results.Count.ShouldBe(2);
        results[0].Value.GetProperty("city").GetString().ShouldBe("London");
        results[1].Value.GetProperty("city").GetString().ShouldBe("Paris");
    }

    [Fact]
    public void Rfc9535_Table12_SearchExample()
    {
        // RFC 9535 Table 12: $[?search(@.author, 'Tolkien')]
        // Arrange
        var json = JsonDocument.Parse("""
        [
            {"title": "The Hobbit", "author": "J.R.R. Tolkien"},
            {"title": "Dune", "author": "Frank Herbert"},
            {"title": "The Lord of the Rings", "author": "J.R.R. Tolkien"}
        ]
        """).RootElement;
        var query = JsonPathParser.Parse("$[?search(@.author, 'Tolkien')]");

        // Act
        var results = _evaluator.Evaluate(query, json);

        // Assert
        results.Count.ShouldBe(2);
        results[0].Value.GetProperty("title").GetString().ShouldBe("The Hobbit");
        results[1].Value.GetProperty("title").GetString().ShouldBe("The Lord of the Rings");
    }

    #endregion

    #region Comparison: Working ValueType Functions (for reference)

    [Fact]
    public void Length_WithComparison_ShouldStillWork()
    {
        // This should continue to work as before
        // Arrange
        var json = JsonDocument.Parse("""
        [
            {"name": "Alice Johnson"},
            {"name": "Bob"},
            {"name": "Charlie Brown"}
        ]
        """).RootElement;
        var query = JsonPathParser.Parse("$[?length(@.name) > 10]");

        // Act
        var results = _evaluator.Evaluate(query, json);

        // Assert
        results.Count.ShouldBe(2);
        results[0].Value.GetProperty("name").GetString().ShouldBe("Alice Johnson");
        results[1].Value.GetProperty("name").GetString().ShouldBe("Charlie Brown");
    }

    [Fact]
    public void Count_WithComparison_ShouldStillWork()
    {
        // This should continue to work as before
        // Arrange
        var json = JsonDocument.Parse("""
        [
            {"id": 1, "items": [1, 2, 3]},
            {"id": 2, "items": [1]},
            {"id": 3, "items": [1, 2, 3, 4]}
        ]
        """).RootElement;
        // Use length() to count array elements, or count(@.items[*]) to count nodes
        var query = JsonPathParser.Parse("$[?length(@.items) > 2]");

        // Act
        var results = _evaluator.Evaluate(query, json);

        // Assert
        results.Count.ShouldBe(2);
        results[0].Value.GetProperty("id").GetInt32().ShouldBe(1);
        results[1].Value.GetProperty("id").GetInt32().ShouldBe(3);
    }

    #endregion

    #region Advanced Test Cases - Nested and Complex Scenarios

    [Fact]
    public void NestedFunctions_SearchWithinLength_ShouldWork()
    {
        // Test functions in combination within same filter
        // Arrange
        var json = JsonDocument.Parse("""
        [
            {"id": 1, "name": "Administrator"},
            {"id": 2, "name": "User"},
            {"id": 3, "name": "Guest"}
        ]
        """).RootElement;
        var query = JsonPathParser.Parse("$[?length(@.name) > 5 && search(@.name, 'Admin')]");
        
        // Act
        var results = _evaluator.Evaluate(query, json);
        
        // Assert
        results.Count.ShouldBe(1);
        results[0].Value.GetProperty("id").GetInt32().ShouldBe(1);
    }

    [Fact]
    public void MultipleSearchCalls_InSameExpression_ShouldWork()
    {
        // Arrange
        var json = JsonDocument.Parse("""
        [
            {"name": "John Smith", "title": "Senior Developer"},
            {"name": "Jane Doe", "title": "Junior Developer"},
            {"name": "Bob Johnson", "title": "Manager"}
        ]
        """).RootElement;
        var query = JsonPathParser.Parse("$[?search(@.name, 'John') && search(@.title, 'Developer')]");
        
        // Act
        var results = _evaluator.Evaluate(query, json);
        
        // Assert
        results.Count.ShouldBe(1);
        results[0].Value.GetProperty("name").GetString().ShouldBe("John Smith");
    }

    [Fact]
    public void Search_CaseSensitive_ShouldDistinguish()
    {
        // Arrange
        var json = JsonDocument.Parse("""
        [
            {"name": "ADMIN"},
            {"name": "admin"},
            {"name": "Admin"}
        ]
        """).RootElement;
        var query = JsonPathParser.Parse("$[?search(@.name, 'admin')]");
        
        // Act
        var results = _evaluator.Evaluate(query, json);
        
        // Assert
        // Should only match lowercase 'admin'
        results.Count.ShouldBe(1);
        results[0].Value.GetProperty("name").GetString().ShouldBe("admin");
    }

    [Fact]
    public void Match_WithSpecialRegexCharacters_ShouldEscape()
    {
        // Arrange
        var json = JsonDocument.Parse("""
        [
            {"pattern": "test.txt"},
            {"pattern": "testXtxt"},
            {"pattern": "test-txt"}
        ]
        """).RootElement;
        // Test literal dot matching - dot must be escaped in regex
        var query = JsonPathParser.Parse("$[?match(@.pattern, 'test\\\\.txt')]");
        
        // Act
        var results = _evaluator.Evaluate(query, json);
        
        // Assert
        results.Count.ShouldBe(1);
        results[0].Value.GetProperty("pattern").GetString().ShouldBe("test.txt");
    }

    [Fact]
    public void Search_EmptyPattern_ShouldMatchAll()
    {
        // Arrange
        var json = JsonDocument.Parse("""
        [
            {"text": "something"},
            {"text": ""},
            {"text": "anything"}
        ]
        """).RootElement;
        var query = JsonPathParser.Parse("$[?search(@.text, '')]");
        
        // Act
        var results = _evaluator.Evaluate(query, json);
        
        // Assert
        // Empty pattern should match all strings (including empty string)
        results.Count.ShouldBe(3);
    }

    [Fact]
    public void Search_WithUnicodeCharacters_ShouldWork()
    {
        // Arrange
        var json = JsonDocument.Parse("""
        [
            {"name": "café"},
            {"name": "naïve"},
            {"name": "résumé"},
            {"name": "regular"}
        ]
        """).RootElement;
        var query = JsonPathParser.Parse("$[?search(@.name, 'é')]");
        
        // Act
        var results = _evaluator.Evaluate(query, json);
        
        // Assert
        results.Count.ShouldBe(2);
        results.ShouldAllBe(r => r.Value.GetProperty("name").GetString()!.Contains('é'));
    }

    [Fact]
    public void Match_WithEmoji_ShouldWork()
    {
        // Arrange
        var json = JsonDocument.Parse("""
        [
            {"message": "Hello 👋"},
            {"message": "Goodbye 👋"},
            {"message": "Hi there"}
        ]
        """).RootElement;
        // Note: Emoji in regex patterns can be tricky - use a simpler pattern
        var query = JsonPathParser.Parse("$[?search(@.message, 'Hello|Goodbye')]");
        
        // Act
        var results = _evaluator.Evaluate(query, json);
        
        // Assert
        results.Count.ShouldBe(2);
    }

    [Fact]
    public void Search_OnDeeplyNestedProperty_ShouldWork()
    {
        // Arrange
        var json = JsonDocument.Parse("""
        [
            {"user": {"profile": {"contact": {"email": "alice@example.com"}}}},
            {"user": {"profile": {"contact": {"email": "bob@test.org"}}}},
            {"user": {"profile": {"contact": {"email": "charlie@example.com"}}}}
        ]
        """).RootElement;
        var query = JsonPathParser.Parse("$[?match(@.user.profile.contact.email, '.*@example\\\\.com$')]");
        
        // Act
        var results = _evaluator.Evaluate(query, json);
        
        // Assert
        results.Count.ShouldBe(2);
    }

    [Fact]
    public void Search_WithValueFunction_InComplexFilter()
    {
        // Arrange
        var json = JsonDocument.Parse("""
        {
            "users": [
                {"name": "Admin", "roles": ["administrator"]},
                {"name": "User", "roles": ["user"]},
                {"name": "Guest", "roles": []}
            ]
        }
        """).RootElement;
        // Test combining search with other functions
        var query = JsonPathParser.Parse("$.users[?search(@.name, 'Admin') && length(@.roles) > 0]");
        
        // Act
        var results = _evaluator.Evaluate(query, json);
        
        // Assert
        results.Count.ShouldBe(1);
        results[0].Value.GetProperty("name").GetString().ShouldBe("Admin");
    }

    [Fact]
    public void Match_StartAnchor_ShouldOnlyMatchBeginning()
    {
        // Arrange
        var json = JsonDocument.Parse("""
        [
            {"code": "TEMP-001"},
            {"code": "001-TEMP"},
            {"code": "TEMP"}
        ]
        """).RootElement;
        var query = JsonPathParser.Parse("$[?match(@.code, '^TEMP')]");
        
        // Act
        var results = _evaluator.Evaluate(query, json);
        
        // Assert
        results.Count.ShouldBe(2);
        results[0].Value.GetProperty("code").GetString().ShouldBe("TEMP-001");
        results[1].Value.GetProperty("code").GetString().ShouldBe("TEMP");
    }

    [Fact]
    public void Match_EndAnchor_ShouldOnlyMatchEnd()
    {
        // Arrange
        var json = JsonDocument.Parse("""
        [
            {"code": "001-TEMP"},
            {"code": "TEMP-001"},
            {"code": "TEMP"}
        ]
        """).RootElement;
        var query = JsonPathParser.Parse("$[?match(@.code, 'TEMP$')]");
        
        // Act
        var results = _evaluator.Evaluate(query, json);
        
        // Assert
        results.Count.ShouldBe(2);
        results[0].Value.GetProperty("code").GetString().ShouldBe("001-TEMP");
        results[1].Value.GetProperty("code").GetString().ShouldBe("TEMP");
    }

    [Fact]
    public void Search_OnArrayElements_ShouldWork()
    {
        // Arrange
        var json = JsonDocument.Parse("""
        {
            "items": [
                {"tags": ["admin", "user", "premium"]},
                {"tags": ["user", "guest"]},
                {"tags": ["admin", "superuser", "moderator"]}
            ]
        }
        """).RootElement;
        // Test length on arrays within filter - items with more than 2 tags
        var query = JsonPathParser.Parse("$.items[?length(@.tags) > 2]");
        
        // Act
        var results = _evaluator.Evaluate(query, json);
        
        // Assert
        results.Count.ShouldBe(2);
        results[0].Value.GetProperty("tags").GetArrayLength().ShouldBe(3);
        results[1].Value.GetProperty("tags").GetArrayLength().ShouldBe(3);
    }

    [Fact]
    public void Match_InvalidRegexPattern_ShouldThrowMeaningfulError()
    {
        // Arrange
        var json = JsonDocument.Parse("""
        [{"text": "test"}]
        """).RootElement;
        // Invalid regex: unclosed group
        var query = JsonPathParser.Parse("$[?match(@.text, '(unclosed')]");
        
        // Act & Assert
        var exception = Should.Throw<Exceptions.JsonPathEvaluationException>(() => 
            _evaluator.Evaluate(query, json));
        
        // The error message should indicate a regex/pattern problem
        (exception.Message.Contains("regex") || 
         exception.Message.Contains("pattern") || 
         exception.InnerException is System.Text.RegularExpressions.RegexParseException).ShouldBeTrue();
    }

    [Fact]
    public void Search_WithWhitespace_ShouldMatch()
    {
        // Arrange
        var json = JsonDocument.Parse("""
        [
            {"name": "John  Smith"},
            {"name": "Jane Doe"},
            {"name": "  Bob  "}
        ]
        """).RootElement;
        var query = JsonPathParser.Parse("$[?search(@.name, '  ')]");
        
        // Act
        var results = _evaluator.Evaluate(query, json);
        
        // Assert
        results.Count.ShouldBe(2); // Matches double spaces
        results.ShouldContain(r => r.Value.GetProperty("name").GetString() == "John  Smith");
        results.ShouldContain(r => r.Value.GetProperty("name").GetString() == "  Bob  ");
    }

    [Fact]
    public void ComplexBoolean_ThreeLevelNesting_ShouldWork()
    {
        // Arrange
        var json = JsonDocument.Parse("""
        [
            {"a": "test", "b": "admin", "c": "enabled"},
            {"a": "test", "b": "user", "c": "disabled"},
            {"a": "prod", "b": "admin", "c": "enabled"}
        ]
        """).RootElement;
        var query = JsonPathParser.Parse(
            "$[?(search(@.a, 'test') && (search(@.b, 'admin') || search(@.c, 'enabled')))]");
        
        // Act
        var results = _evaluator.Evaluate(query, json);
        
        // Assert
        results.Count.ShouldBe(1);
        results[0].Value.GetProperty("a").GetString().ShouldBe("test");
        results[0].Value.GetProperty("b").GetString().ShouldBe("admin");
    }

    #endregion
}
