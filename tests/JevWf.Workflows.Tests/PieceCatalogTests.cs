using FluentAssertions;
using JevWf.Workflows.Catalog;

namespace JevWf.Workflows.Tests;

public sealed class PieceCatalogTests
{
    private static readonly PieceCatalog Vtex = PieceCatalog.Load(TestPaths.Catalog);

    [Fact]
    public void A_replacing_chain_takes_the_place_and_public_status_of_the_replaced_piece()
    {
        var pieces = Vtex.For("seller-pharmacy-express").Select(p => p.Name).ToList();

        pieces.Should().NotContain("start_handling");
        pieces.Should().ContainInOrder("reserve_stock", "pick_from_shelf", "pack", "repack", "issue_invoice", "ship_standard");
        Vtex.For("seller-pharmacy-express").Where(p => p.Replaces == "start_handling")
            .Should().OnlyContain(p => p.PublicStatus == "handling" && p.Source == "seller-pharmacy-express");
    }

    [Fact]
    public void Unhandled_exceptions_inside_a_chain_leave_through_the_replaced_piece_exception()
    {
        var pieces = Vtex.For("seller-pharmacy-express").ToDictionary(p => p.Name);

        pieces["pick_from_shelf"].OnException.Should().Be("resourcing_required");
        pieces["pack"].OnException.Should().Be("pack_failed");
    }

    [Fact]
    public void Sellers_without_custom_pieces_use_the_vtex_catalog() =>
        Vtex.For("seller-unknown").Should().BeSameAs(Vtex.Vtex);

    [Fact]
    public void A_chain_must_leave_through_the_same_types_as_the_replaced_piece()
    {
        var chain = Piece("fast_handling", "stock_reserved", ("done", "packed")) with { Replaces = "start_handling" };

        var load = () => WithSeller(chain);

        load.Should().Throw<InvalidDataException>().WithMessage("*leaves through [packed]*start_handling*[ready_to_ship]*");
    }

    [Fact]
    public void A_chain_must_accept_the_same_types_as_the_replaced_piece()
    {
        var chain = Piece("fast_handling", "picked", ("done", "ready_to_ship")) with { Replaces = "start_handling" };

        var load = () => WithSeller(chain);

        load.Should().Throw<InvalidDataException>().WithMessage("*must accept [stock_reserved]*");
    }

    [Fact]
    public void A_chain_cannot_fail_through_a_new_external_type()
    {
        var chain = Piece("fast_handling", "stock_reserved", ("done", "ready_to_ship"))
            with { Replaces = "start_handling", OnException = "somewhere_else" };

        var load = () => WithSeller(chain);

        load.Should().Throw<InvalidDataException>().WithMessage("*can only fail through 'resourcing_required'*");
    }

    [Fact]
    public void A_custom_piece_cannot_reuse_a_vtex_name()
    {
        var load = () => WithSeller(Piece("payment", "item_released", ("ok", "item_released")) with { PublicStatus = "sourcing" });

        load.Should().Throw<InvalidDataException>().WithMessage("*'payment' is already used*");
    }

    [Fact]
    public void An_added_custom_piece_must_use_a_vtex_public_status()
    {
        var load = () => WithSeller(Piece("gift_wrap", "stock_reserved", ("wrapped", "stock_reserved")) with { PublicStatus = "wrapping" });

        load.Should().Throw<InvalidDataException>().WithMessage("*must use one of the VTEX public statuses*");
    }

    internal static PieceDefinition Piece(string name, string accepts, params (string Port, string Type)[] outputs) => new()
    {
        Name = name,
        In = [accepts],
        Out = outputs.ToDictionary(o => o.Port, o => o.Type)
    };

    internal static PieceCatalog WithSeller(params PieceDefinition[] pieces) =>
        new(Vtex.Types, Vtex.Vtex, new Dictionary<string, IReadOnlyList<PieceDefinition>> { ["seller-x"] = pieces });
}
