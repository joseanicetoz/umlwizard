using UmlWizard.CodeImport.Cpp;
using UmlWizard.CodeImport.Java;
using UmlWizard.CodeImport.Kotlin;
using UmlWizard.CodeImport.Ruby;
using UmlWizard.CodeImport.TypeScript;
using UmlWizard.Core.Model;

namespace UmlWizard.CodeImport.Tests;

/// <summary>Idiomatic code of each language, read into a class diagram.</summary>
public sealed class LanguageTests
{
    private static (ClassDiagramModel Model, CodeImportResult Result) Import(ICodeImporter importer, params (string Path, string Text)[] files)
    {
        var folder = Directory.CreateTempSubdirectory("umlwizard-lang-").FullName;
        try
        {
            foreach (var (path, text) in files)
            {
                var full = Path.Combine(folder, path);
                Directory.CreateDirectory(Path.GetDirectoryName(full)!);
                File.WriteAllText(full, text);
            }

            var result = CodeImporter.Import(SourceFolder.Read(folder, importer.FileExtensions), importer);
            return ((ClassDiagramModel)result.Diagram!.Model, result);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    private static UmlClass Class(ClassDiagramModel model, string name) => model.Classes.Single(c => c.Name == name);

    private static UmlRelationship? Relationship(ClassDiagramModel model, string source, string target) =>
        model.Relationships.SingleOrDefault(r => r.SourceId == Class(model, source).Id && r.TargetId == Class(model, target).Id);

    [Fact]
    public void Java_ClassesInterfacesEnumsAndRecords()
    {
        var (model, result) = Import(new JavaCodeImporter(),
            ("com/shop/Entity.java", """
                package com.shop;

                import java.util.UUID;

                /** The base of every entity. */
                public abstract class Entity {
                    protected UUID id;
                    public abstract boolean validate();
                }
                """),
            ("com/shop/Order.java", """
                package com.shop;

                import java.util.*;
                import java.util.function.Function;

                @Entity(name = "orders")
                public class Order extends Entity implements Payable, Comparable<Order> {
                    public static final int MAX_LINES = 100;
                    private final List<OrderLine> lines = new ArrayList<>();
                    private Optional<Customer> customer = Optional.empty();
                    private OrderStatus status = OrderStatus.NEW;
                    private Map<String, OrderLine> byCode = new HashMap<>();
                    private final Function<Integer, Integer> twice = x -> { return x * 2; };
                    int count, total[];

                    static { System.out.println("loaded"); }

                    public Order(String number) { this.number = number; }

                    @Override
                    public boolean validate() { return !lines.isEmpty(); }

                    public <T extends Comparable<T>> T max(List<T> values) { return null; }

                    public void addLine(OrderLine line, int... quantities) throws IllegalStateException {
                        Runnable r = new Runnable() { public void run() { } };
                        lines.add(line);
                    }

                    @Override public boolean pay(double amount) { return true; }

                    public int compareTo(Order other) { return 0; }

                    static class Line { String code; }
                }
                """),
            ("com/shop/Payable.java", """
                package com.shop;
                public interface Payable {
                    double TAX = 0.2;
                    boolean pay(double amount);
                    default boolean isFree() { return false; }
                }
                """),
            ("com/shop/OrderStatus.java", """
                package com.shop;
                public enum OrderStatus {
                    NEW("n"), PAID("p") { @Override public String toString() { return "paid"; } }, SHIPPED("s");
                    private final String code;
                    OrderStatus(String code) { this.code = code; }
                }
                """),
            ("com/shop/Customer.java", "package com.shop; public record Customer(String name, String email) implements Payable { public boolean pay(double a) { return true; } }"),
            ("com/shop/OrderLine.java", "package com.shop; class OrderLine { int quantity; }"),
            ("com/shop/Shop.kt", "class NotJava"));

        Assert.Equal(ClassifierKind.AbstractClass, Class(model, "Entity").Kind);
        Assert.Equal(ClassifierKind.Interface, Class(model, "Payable").Kind);
        Assert.Equal("record", Class(model, "Customer").Stereotype);
        Assert.Equal("com.shop", Class(model, "Order").TaggedValues.Get(CodeImporter.NamespaceTag));

        var order = Class(model, "Order");
        Assert.Equal(["MAX_LINES", "status", "twice", "count", "total"], order.Attributes.Select(a => a.Name));
        Assert.Equal(("int", UmlVisibility.Public, true, "100"), (order.Attributes[0].Type, order.Attributes[0].Visibility, order.Attributes[0].IsStatic, order.Attributes[0].DefaultValue));
        Assert.Equal(UmlVisibility.Package, order.Attributes.Single(a => a.Name == "count").Visibility);
        Assert.Equal("int[]", order.Attributes.Single(a => a.Name == "total").Type);
        Assert.Equal(["Order", "validate", "max<T>", "addLine", "pay", "compareTo"], order.Operations.Select(o => o.Name));
        Assert.Null(order.Operations[0].ReturnType);
        Assert.Equal([("line", "OrderLine"), ("quantities", "int...")], order.Operations[3].Parameters.Select(p => (p.Name, p.Type)));

        Assert.Equal(RelationshipKind.Inheritance, Relationship(model, "Order", "Entity")!.Kind);
        Assert.Equal(RelationshipKind.Realization, Relationship(model, "Order", "Payable")!.Kind);
        Assert.Equal(RelationshipKind.Realization, Relationship(model, "Customer", "Payable")!.Kind);
        var lines = Relationship(model, "Order", "OrderLine")!;
        Assert.Equal(("lines, byCode", "*"), (lines.Label, lines.TargetMultiplicity));
        Assert.Equal("0..1", Relationship(model, "Order", "Customer")!.TargetMultiplicity);

        Assert.Equal([("NEW", "\"n\""), ("PAID", "\"p\""), ("SHIPPED", "\"s\"")], Class(model, "OrderStatus").Attributes.Where(a => a.Name.All(char.IsUpper)).Select(a => (a.Name, a.DefaultValue)));
        Assert.True(Class(model, "Payable").Attributes.Single().IsStatic);
        Assert.NotNull(Class(model, "Order.Line"));

        var notes = string.Join("\n", result.Notes);
        Assert.Contains("1 other file was skipped: 1 .kt.", notes);
        Assert.Contains("Base types that are not part of the import are not shown: Comparable<Order>.", notes);
        Assert.Contains("annotations (e.g. @Override) were left out", notes);
        Assert.Contains("1 initializer block was left out", notes);
        Assert.Contains("1 bound of a type parameter (extends …) was left out", notes);
        Assert.DoesNotContain("could not be read", notes);
    }

    [Fact]
    public void Cpp_ClassesStructsEnumsAndInterfaces()
    {
        var (model, result) = Import(new CppCodeImporter(),
            ("include/shop/order.h", """
                #pragma once
                #include <memory>
                #include <vector>
                #define SHOP_API __declspec(dllexport)

                namespace shop::domain {

                class Customer;

                /// Something that can be paid.
                class IPayable {
                public:
                    virtual ~IPayable() = default;
                    virtual bool pay(double amount) = 0;
                };

                class Entity {
                protected:
                    long id_ = 0;
                public:
                    virtual ~Entity() {}
                    virtual bool validate() const = 0;
                    long id() const { return id_; }
                };

                enum class OrderStatus : int { New = 1, Paid, Shipped };

                struct Money { double amount; std::string currency{"EUR"}; };

                class SHOP_API Order final : public Entity, public IPayable {
                    friend class OrderPrinter;
                    using Lines = std::vector<std::unique_ptr<OrderLine>>;
                public:
                    static const int MaxLines = 100;
                    explicit Order(std::string number, int priority = 1);
                    Order(const Order&) = delete;
                    ~Order() override;
                    bool validate() const override;
                    bool pay(double amount) override { return true; }
                    Order& operator=(const Order&) = delete;
                    template <typename T> T get(const std::string& key) const;
                    std::vector<std::unique_ptr<OrderLine>> lines;
                private:
                    Customer* customer_ = nullptr;
                    OrderStatus status_ = OrderStatus::New;
                    std::map<std::string, OrderLine*> byCode_;
                    int a_, *b_, c_[4];
                };

                template <typename T>
                class Repository {
                public:
                    void add(const T& item);
                private:
                    std::vector<T> items_;
                };

                }  // namespace shop::domain
                """),
            ("src/order.cpp", """
                #include "shop/order.h"
                namespace shop::domain {
                Order::Order(std::string number, int priority) : Entity(), lines{}, customer_(nullptr) { }
                bool Order::validate() const { return !lines.empty(); }
                static int helper(int x) { return x * 2; }
                class OrderLine { public: int quantity = 1; };
                class Customer { public: std::string name; };
                }
                """),
            ("README.md", "# Shop"));

        Assert.Equal(ClassifierKind.Interface, Class(model, "IPayable").Kind);
        Assert.Equal(ClassifierKind.AbstractClass, Class(model, "Entity").Kind);
        Assert.Equal(ClassifierKind.Enumeration, Class(model, "OrderStatus").Kind);
        Assert.Equal([("New", "1"), ("Paid", null), ("Shipped", null)], Class(model, "OrderStatus").Attributes.Select(a => (a.Name, a.DefaultValue)));
        Assert.Equal("struct", Class(model, "Money").Stereotype);
        Assert.Equal(UmlVisibility.Public, Class(model, "Money").Attributes[0].Visibility);
        Assert.Equal("shop::domain", Class(model, "Order").TaggedValues.Get(CodeImporter.NamespaceTag));
        Assert.Single(model.Classes, c => c.Name == "Order");

        var order = Class(model, "Order");
        Assert.Equal(["MaxLines", "status_", "a_", "b_", "c_"], order.Attributes.Select(a => a.Name));
        Assert.Equal(["Customer", "Entity", "IPayable", "Money", "Order", "OrderLine", "OrderStatus", "Repository<T>"], model.Classes.Select(c => c.Name).Order());
        Assert.Equal(("const int", UmlVisibility.Public, true, "100"), (order.Attributes[0].Type, order.Attributes[0].Visibility, order.Attributes[0].IsStatic, order.Attributes[0].DefaultValue));
        Assert.Equal(("int*", "int[4]"), (order.Attributes[3].Type, order.Attributes[4].Type));
        Assert.Equal(["Order", "Order", "validate", "pay", "get<T>"], order.Operations.Select(o => o.Name));
        Assert.Equal([("number", "std::string"), ("priority", "int")], order.Operations[0].Parameters.Select(p => (p.Name, p.Type)));
        Assert.Equal("bool", order.Operations[2].ReturnType);
        Assert.True(Class(model, "Entity").Operations.Single(o => o.Name == "validate").IsAbstract);

        Assert.Equal(RelationshipKind.Inheritance, Relationship(model, "Order", "Entity")!.Kind);
        Assert.Equal(RelationshipKind.Realization, Relationship(model, "Order", "IPayable")!.Kind);
        var lines = Relationship(model, "Order", "OrderLine")!;
        Assert.Equal(("lines, byCode_", "*"), (lines.Label, lines.TargetMultiplicity));
        Assert.Equal(("customer_", "0..1"), (Relationship(model, "Order", "Customer")?.Label, Relationship(model, "Order", "Customer")?.TargetMultiplicity));
        Assert.Equal("Repository<T>", model.Classes.Single(c => c.Name.StartsWith("Repository")).Name);

        var notes = string.Join("\n", result.Notes);
        Assert.Contains("Preprocessor directives (#include, #define, #if, …) were not run", notes);
        Assert.Contains("1 function or variable outside a class was left out", notes);
        Assert.Contains("destructors were left out", notes);
        Assert.Contains("1 operator was left out", notes);
        Assert.Contains("1 friend declaration was left out", notes);
        Assert.Contains("1 type alias (typedef, using) was left out", notes);
        Assert.Contains("1 parameter lost its default argument", notes);
        Assert.DoesNotContain("could not be read", notes);
    }

    [Fact]
    public void Ruby_ClassesModulesMixinsAndAttributes()
    {
        var (model, result) = Import(new RubyCodeImporter(),
            ("lib/shop/payable.rb", """
                module Shop
                  # Something that can be paid.
                  module Payable
                    TAX = 0.2

                    def pay(amount)
                      raise NotImplementedError, "#{self.class} must implement pay"
                    end
                  end
                end
                """),
            ("lib/shop/order.rb", """
                require "json"

                module Shop
                  class Order < Entity
                    include Payable
                    include Comparable

                    attr_accessor :number, :status
                    attr_reader :lines

                    MAX_LINES = 100
                    @@count = 0

                    def initialize(number, customer: nil, *lines)
                      @number = number
                      @customer = customer
                      @lines = lines
                      @@count += 1
                    end

                    def total
                      lines.sum { |line| line.price * line.quantity }
                    end

                    def self.find(id) = all.find { |o| o.id == id }

                    def to_s
                      <<~TEXT
                        Order #{number}
                        end
                        def not_a_method
                      TEXT
                    end

                    def pay(amount)
                      return false if amount <= 0
                      if amount > total
                        refund(amount - total)
                      end
                      [1, 2].each do |i|
                        puts i
                      end
                      true
                    end

                    class << self
                      def all
                        @all ||= []
                      end
                    end

                    private

                    def refund(amount)
                      puts "refund"
                    end

                    define_method(:dynamic) { 42 }
                  end

                  Point = Struct.new(:x, :y)
                end
                """),
            ("lib/shop/entity.rb", """
                module Shop
                  class Entity
                    attr_reader :id
                    def validate
                      raise NotImplementedError
                    end
                  end
                end
                """),
            ("lib/shop/order_extensions.rb", """
                module Shop
                  class Order
                    def cancel; @status = :cancelled; end
                  end
                end
                """),
            ("Gemfile", "source 'https://rubygems.org'"));

        Assert.Equal(["Entity", "Order", "Payable", "Point"], model.Classes.Select(c => c.Name).Order(StringComparer.Ordinal));
        var payable = Class(model, "Payable");
        Assert.Equal((ClassifierKind.Interface, "module"), (payable.Kind, payable.Stereotype));
        Assert.True(payable.Operations.Single().IsAbstract);
        Assert.Equal(ClassifierKind.AbstractClass, Class(model, "Entity").Kind);
        Assert.Equal("Shop", Class(model, "Order").TaggedValues.Get(CodeImporter.NamespaceTag));

        var order = Class(model, "Order");
        Assert.Equal(ClassifierKind.Class, order.Kind);
        Assert.Equal(["number", "status", "lines", "MAX_LINES", "count", "customer", "all"], order.Attributes.Select(a => a.Name));
        Assert.True(order.Attributes[6].IsStatic);
        Assert.Equal((UmlVisibility.Public, true, "100"), (order.Attributes[3].Visibility, order.Attributes[3].IsStatic, order.Attributes[3].DefaultValue));
        Assert.Equal((UmlVisibility.Private, true), (order.Attributes[4].Visibility, order.Attributes[4].IsStatic));
        Assert.Equal(UmlVisibility.Private, order.Attributes[5].Visibility);
        Assert.Equal(["Order", "total", "find", "to_s", "pay", "all", "refund", "cancel"], order.Operations.Select(o => o.Name));
        Assert.Equal(["number", "customer", "lines"], order.Operations[0].Parameters.Select(p => p.Name));
        Assert.True(order.Operations.Single(o => o.Name == "find").IsStatic);
        Assert.True(order.Operations.Single(o => o.Name == "all").IsStatic);
        Assert.Equal(UmlVisibility.Private, order.Operations.Single(o => o.Name == "refund").Visibility);
        Assert.Equal(UmlVisibility.Public, order.Operations.Single(o => o.Name == "cancel").Visibility);

        Assert.Equal(RelationshipKind.Inheritance, Relationship(model, "Order", "Entity")!.Kind);
        Assert.Equal(RelationshipKind.Realization, Relationship(model, "Order", "Payable")!.Kind);
        Assert.Equal(["x", "y"], Class(model, "Point").Attributes.Select(a => a.Name));
        Assert.Equal("struct", Class(model, "Point").Stereotype);

        var notes = string.Join("\n", result.Notes);
        Assert.Contains("1 other file was skipped: 1 (no extension).", notes);
        Assert.Contains("Ruby declares no types", notes);
        Assert.Contains("Base types that are not part of the import are not shown: Comparable.", notes);
        Assert.Contains("1 method defined at run time (define_method, method_missing, …) was left out", notes);
        Assert.Contains("1 parameter lost its default value", notes);
        Assert.DoesNotContain("unclosed", notes);
    }

    [Fact]
    public void TypeScript_ClassesInterfacesEnumsAndNamespaces()
    {
        var (model, result) = Import(new TypeScriptCodeImporter(),
            ("src/models/entity.ts", """
                import { v4 as uuid } from 'uuid'
                import type { Payable } from './payable'

                /** The base of every entity. */
                export abstract class Entity {
                  protected readonly id: string = uuid()
                  abstract validate(): boolean
                }
                """),
            ("src/models/order.ts", """
                import { Entity } from './entity'
                import type { Payable } from './payable'
                import { OrderStatus } from './order-status'

                const TAX = 0.2
                export function total(lines: OrderLine[]): number { return lines.reduce((s, l) => s + l.price, 0) }

                @Entity({ name: 'orders' })
                export class Order extends Entity implements Payable, Comparable<Order> {
                  static readonly MAX_LINES = 100
                  private lines: OrderLine[] = []
                  customer?: Customer
                  status: OrderStatus = OrderStatus.New
                  #secret = `order-${Date.now()}-{not a brace}`
                  private byCode = new Map<string, OrderLine>()
                  readonly tags: Array<string> = []
                  private pattern = /[{}]"'/g
                  handler = (e: Event): void => {
                    console.log(`clicked ${e.type} at ${`{${e.timeStamp}}`}`)
                  }

                  constructor(public readonly number: string, private repo: Repository<Order>, priority = 1) {
                    super()
                  }

                  get total(): number { return this.lines.length }
                  set total(value: number) { }

                  validate(): boolean { return this.lines.length > 0 }

                  add(line: OrderLine): void
                  add(line: OrderLine, quantity: number): void
                  add(line: OrderLine, quantity?: number): void {
                    if (quantity) { this.lines.push(line) }
                  }

                  async pay(amount: number): Promise<boolean> { return true }

                  static create<T extends Order>(this: void, ...lines: OrderLine[]): Order { return new Order('1', null!) }

                  [Symbol.iterator]() { return this.lines[Symbol.iterator]() }

                  compareTo(other: Order) { return 0 }
                }
                """),
            ("src/models/payable.ts", """
                export interface Payable {
                  readonly currency: string
                  pay(amount: number): Promise<boolean>
                  refund?(amount: number): void
                  [key: string]: unknown
                  (): void
                }

                export type Money = { amount: number; currency: string }
                """),
            ("src/models/order-status.ts", """
                export enum OrderStatus { New = 'NEW', Paid = 'PAID', Shipped = 'SHIPPED' }
                export const enum Priority { Low = 1, High }
                """),
            ("src/models/customer.ts", """
                export class Customer {
                  constructor(readonly name: string, public email: string | null = null) {}
                  orders: Order[] = []
                }

                export class OrderLine { quantity = 0; product!: Product }

                namespace Legacy.Shop {
                  export class Product { name: string = '' }
                }

                declare module 'express' {
                  interface Request { user?: Customer }
                }
                """),
            ("src/index.js", "export const notTypeScript = true;"));

        Assert.Equal(ClassifierKind.AbstractClass, Class(model, "Entity").Kind);
        Assert.True(Class(model, "Entity").Operations.Single().IsAbstract);
        Assert.Equal(ClassifierKind.Interface, Class(model, "Payable").Kind);
        Assert.Equal("Legacy.Shop", Class(model, "Product").TaggedValues.Get(CodeImporter.NamespaceTag));

        var order = Class(model, "Order");
        Assert.Equal(["MAX_LINES", "status", "secret", "byCode", "tags", "pattern", "handler", "number", "repo", "total"], order.Attributes.Select(a => a.Name));
        Assert.Equal((UmlVisibility.Public, true, "100"), (order.Attributes[0].Visibility, order.Attributes[0].IsStatic, order.Attributes[0].DefaultValue));
        Assert.Equal(UmlVisibility.Private, order.Attributes.Single(a => a.Name == "secret").Visibility);
        Assert.Equal(("Repository<Order>", UmlVisibility.Private), (order.Attributes.Single(a => a.Name == "repo").Type, order.Attributes.Single(a => a.Name == "repo").Visibility));
        Assert.Equal("number", order.Attributes.Single(a => a.Name == "total").Type);
        Assert.Equal(["Order", "validate", "add", "add", "pay", "create<T>", "[Symbol.iterator]", "compareTo"], order.Operations.Select(o => o.Name));
        Assert.Equal([("number", "string"), ("repo", "Repository<Order>"), ("priority", null)], order.Operations[0].Parameters.Select(p => (p.Name, p.Type)));
        Assert.Equal("Promise<boolean>", order.Operations.Single(o => o.Name == "pay").ReturnType);
        var create = order.Operations.Single(o => o.Name == "create<T>");
        Assert.True(create.IsStatic);
        Assert.Equal([("lines", "OrderLine[]")], create.Parameters.Select(p => (p.Name, p.Type)));

        Assert.Equal(RelationshipKind.Inheritance, Relationship(model, "Order", "Entity")!.Kind);
        Assert.Equal(RelationshipKind.Realization, Relationship(model, "Order", "Payable")!.Kind);
        Assert.Equal(("lines", "*"), (Relationship(model, "Order", "OrderLine")!.Label, Relationship(model, "Order", "OrderLine")!.TargetMultiplicity));
        Assert.Equal("0..1", Relationship(model, "Order", "Customer")!.TargetMultiplicity);
        Assert.Equal("*", Relationship(model, "Customer", "Order")!.TargetMultiplicity);
        Assert.NotNull(Relationship(model, "OrderLine", "Product"));
        Assert.Equal("0..1", Relationship(model, "Request", "Customer")!.TargetMultiplicity);

        Assert.Equal(["currency"], Class(model, "Payable").Attributes.Select(a => a.Name));
        Assert.Equal(["pay", "refund"], Class(model, "Payable").Operations.Select(o => o.Name));
        Assert.Equal([("New", "'NEW'"), ("Paid", "'PAID'"), ("Shipped", "'SHIPPED'")], Class(model, "OrderStatus").Attributes.Select(a => (a.Name, a.DefaultValue)));
        Assert.Equal([("Low", "1"), ("High", null)], Class(model, "Priority").Attributes.Select(a => (a.Name, a.DefaultValue)));
        Assert.Equal(["name", "email"], Class(model, "Customer").Attributes.Select(a => a.Name));

        var notes = string.Join("\n", result.Notes);
        Assert.StartsWith("Only TypeScript source files (.ts, .tsx, .mts, .cts) are read", result.Notes[0]);
        Assert.Contains("1 other file was skipped: 1 .js.", notes);
        Assert.Contains("Base types that are not part of the import are not shown: Comparable<Order>.", notes);
        Assert.Contains("2 functions and variables outside classes were left out", notes);
        Assert.Contains("1 type alias (type X = …) was left out", notes);
        Assert.Contains("1 decorator (e.g. @Component) was left out", notes);
        Assert.Contains("1 index signature ([key: string]: …) was left out", notes);
        Assert.Contains("1 call or construct signature of an interface was left out", notes);
        Assert.Contains("4 parameters lost their default values, ? (optional) or ... (rest)", notes);
        Assert.Contains("1 constraint of a type parameter (extends …) was left out", notes);
        Assert.DoesNotContain("could not be read", notes);
    }

    [Fact]
    public void Kotlin_ClassesDataClassesObjectsEnumsAndCompanions()
    {
        var (model, result) = Import(new KotlinCodeImporter(),
            ("src/main/kotlin/com/shop/Entity.kt", """
                package com.shop

                import java.util.UUID

                /** The base of every entity. */
                abstract class Entity(val id: UUID = UUID.randomUUID()) {
                    abstract fun validate(): Boolean
                    open fun describe(): String = "Entity $id"
                }
                """),
            ("src/main/kotlin/com/shop/Order.kt", """
                package com.shop

                import kotlinx.serialization.Serializable

                const val MAX_LINES = 100

                fun Order.totalWithTax(): Double = total() * 1.2

                @Serializable
                class Order(
                    val number: String,
                    private var customer: Customer? = null,
                    priority: Int = 1,
                ) : Entity(), Payable, Comparable<Order> {
                    private val lines: MutableList<OrderLine> = mutableListOf()
                    var status: OrderStatus = OrderStatus.NEW
                        private set
                    internal val byCode: Map<String, OrderLine> = emptyMap()
                    val summary: String
                        get() = "Order $number with ${lines.size} lines: ${lines.joinToString { "{${it.quantity}}" }}"
                    lateinit var note: String
                    private val handler: (Event) -> Unit = { e -> println("clicked $e") }
                    val lazyValue by lazy {
                        computeSomething()
                    }

                    init {
                        require(number.isNotBlank()) { "number" }
                    }

                    constructor(number: String) : this(number, null, 1)

                    override fun validate(): Boolean = lines.isNotEmpty()

                    override fun pay(amount: Double): Boolean {
                        return amount > 0
                    }

                    fun <T : Comparable<T>> max(values: List<T>): T? = values
                        .filter { it != null }
                        .maxOrNull()

                    fun addLine(line: OrderLine, vararg quantities: Int) {
                        lines.add(line)
                    }

                    override fun compareTo(other: Order): Int = number.compareTo(other.number)

                    val String.shouted get() = uppercase()

                    companion object {
                        const val PREFIX = "ORD-"
                        fun create(number: String): Order = Order(number)
                    }

                    inner class Line(val code: String)

                    /* a block comment /* nested */ with } a brace */
                }
                """),
            ("src/main/kotlin/com/shop/Payable.kt", """
                package com.shop

                fun interface Validator { fun validate(value: String): Boolean }

                interface Payable {
                    val currency: String get() = "EUR"
                    fun pay(amount: Double): Boolean
                }

                sealed interface Event {
                    data class Clicked(val x: Int, val y: Int) : Event
                    object Closed : Event
                }
                """),
            ("src/main/kotlin/com/shop/OrderStatus.kt", """
                package com.shop

                enum class OrderStatus(val code: String) {
                    NEW("n"),
                    PAID("p") {
                        override fun toString() = "paid"
                    },
                    SHIPPED("s");

                    fun isFinal() = this == SHIPPED
                }
                """),
            ("src/main/kotlin/com/shop/Customer.kt", """
                package com.shop

                data class Customer(val name: String, val email: String?, val orders: List<Order> = emptyList())

                object Registry {
                    val customers = mutableListOf<Customer>()
                }

                typealias Orders = List<Order>

                annotation class Audited

                @JvmInline
                value class OrderId(val value: String)

                class OrderLine(val quantity: Int, val product: Product)

                class Product { var name: String = "" }
                """),
            ("build.gradle.kts", "plugins { kotlin(\"jvm\") }"));

        Assert.Equal(ClassifierKind.AbstractClass, Class(model, "Entity").Kind);
        Assert.Equal(["validate", "describe"], Class(model, "Entity").Operations.Select(o => o.Name));
        Assert.True(Class(model, "Entity").Operations[0].IsAbstract);
        Assert.Equal("com.shop", Class(model, "Order").TaggedValues.Get(CodeImporter.NamespaceTag));
        Assert.Equal(("data", "object", "value"), (Class(model, "Customer").Stereotype, Class(model, "Registry").Stereotype, Class(model, "OrderId").Stereotype));
        Assert.Equal((ClassifierKind.Interface, "sealed"), (Class(model, "Event").Kind, Class(model, "Event").Stereotype));
        Assert.Equal(ClassifierKind.Interface, Class(model, "Validator").Kind);

        var order = Class(model, "Order");
        Assert.Equal(["number", "status", "summary", "note", "handler", "lazyValue", "PREFIX"], order.Attributes.Select(a => a.Name));
        Assert.Equal(("String", UmlVisibility.Public, false), (order.Attributes[0].Type, order.Attributes[0].Visibility, order.Attributes[0].IsStatic));
        Assert.Equal("(Event) -> Unit", order.Attributes.Single(a => a.Name == "handler").Type);
        Assert.True(order.Attributes.Single(a => a.Name == "PREFIX").IsStatic);
        Assert.Equal(["Order", "Order", "validate", "pay", "max<T>", "addLine", "compareTo", "create"], order.Operations.Select(o => o.Name));
        Assert.Equal([("number", "String"), ("customer", "Customer?"), ("priority", "Int")], order.Operations[0].Parameters.Select(p => (p.Name, p.Type)));
        Assert.Equal("T?", order.Operations.Single(o => o.Name == "max<T>").ReturnType);
        Assert.True(order.Operations.Single(o => o.Name == "create").IsStatic);

        Assert.Equal(RelationshipKind.Inheritance, Relationship(model, "Order", "Entity")!.Kind);
        Assert.Equal(RelationshipKind.Realization, Relationship(model, "Order", "Payable")!.Kind);
        var lines = Relationship(model, "Order", "OrderLine")!;
        Assert.Equal(("lines, byCode", "*"), (lines.Label, lines.TargetMultiplicity));
        Assert.Equal(("customer", "0..1"), (Relationship(model, "Order", "Customer")!.Label, Relationship(model, "Order", "Customer")!.TargetMultiplicity));
        Assert.Equal("*", Relationship(model, "Customer", "Order")!.TargetMultiplicity);
        Assert.Equal(RelationshipKind.Realization, Relationship(model, "Event.Clicked", "Event")!.Kind);
        Assert.Equal(RelationshipKind.Realization, Relationship(model, "Event.Closed", "Event")!.Kind);
        Assert.NotNull(Relationship(model, "OrderLine", "Product"));
        Assert.NotNull(Class(model, "Order.Line"));

        Assert.Equal([("NEW", "\"n\""), ("PAID", "\"p\""), ("SHIPPED", "\"s\""), ("code", null)], Class(model, "OrderStatus").Attributes.Select(a => (a.Name, a.DefaultValue)));
        Assert.Equal(["isFinal"], Class(model, "OrderStatus").Operations.Select(o => o.Name));
        Assert.Equal(["x", "y"], Class(model, "Event.Clicked").Attributes.Select(a => a.Name));
        Assert.Equal(["name", "email"], Class(model, "Customer").Attributes.Select(a => a.Name));
        Assert.DoesNotContain(model.Classes, c => c.Name == "Audited");

        var notes = string.Join("\n", result.Notes);
        Assert.StartsWith("Only Kotlin source files (.kt) are read", result.Notes[0]);
        Assert.Contains("1 other file was skipped: 1 .kts.", notes);
        Assert.Contains("Base types that are not part of the import are not shown: Comparable<Order>.", notes);
        Assert.Contains("1 function or property outside a class was left out", notes);
        Assert.Contains("2 extension functions and properties were left out", notes);
        Assert.Contains("2 annotations (e.g. @Serializable) were left out", notes);
        Assert.Contains("1 annotation class was left out", notes);
        Assert.Contains("1 init block was left out", notes);
        Assert.Contains("1 type alias (typealias) was left out", notes);
        Assert.Contains("3 custom getters and setters were left out", notes);
        Assert.Contains("5 parameters lost their default values or vararg", notes);
        Assert.Contains("1 bound of a type parameter (: … or where …) was left out", notes);
        Assert.DoesNotContain("could not be read", notes);
    }
}
