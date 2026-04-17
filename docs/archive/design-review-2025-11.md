# Design Review (archived, November 2025)

> **Archived — November 2025.** This is a point-in-time audit that drove the restructure into `docs/architecture/`, `docs/reference/`, and the alignment plan in `docs/roadmap/api-alignment-plan.md`. Kept for historical reference. For the current architecture, start at [`docs/architecture/00-overview.md`](../architecture/00-overview.md). Paths and line numbers below reflect the codebase as of late 2025 and may have shifted.

## Executive Summary

This design review analyzes the Hedron MUD engine architecture to identify strengths, weaknesses, and recommended improvements for creating a comprehensive and maintainable design.
The codebase shows promise with a partially-implemented ECS system but suffers from inconsistent architectural patterns, incomplete separations of concerns, and missing event-driven design.

## Architecture Analysis

### Current State Assessment

**Phase:** Transitional - Between Legacy OOP and ECS Architecture
- ECS Phase 1 complete (components extracted)
- ECS Phase 1.5 in progress (entity hierarchy flattening) 
- Legacy OOP patterns still prevalent
- Command system functional but basic
- Combat system working but tightly coupled

### Major Architectural Issues

#### 1. **Hybrid Architecture Complexity**
**Problem:** The codebase simultaneously uses OOP inheritance and ECS composition, creating confusion about which pattern to use when.

**Evidence:** 
- Entity class still uses property accessors that delegate to ECS components (`Entity.cs:46-119`)
- Player class inherits from EntityAnimate while also having ECS component integration
- CommandService uses static dictionary mapping (`CommandService.cs:14-72`)

**Impact:** Developers must understand both paradigms and know which to use for new features.

#### 2. **Missing Event System Architecture**
**Problem:** No centralized event system exists for decoupling game logic and enabling reactive programming patterns.

**Evidence:**
- Combat logic is procedurally executed in `CombatHandler.ProcessEntityAutoAttack`
- Direct method calls between systems (e.g., death handling directly calls `Goto` command)
- No event aggregator or message bus pattern

**Impact:** Tight coupling between systems, difficult testing, hard to extend behavior.

#### 3. **Inconsistent Data Flow Patterns**
**Problem:** Mixed use of pull-based queries and push-based updates without clear guidelines.

**Evidence:**
- IOHandler uses queue-based messaging (`IOHandler.cs`)
- DataAccess provides direct cache access (`DataAccess.cs`)  
- Some systems poll for state changes while others push updates

**Impact:** Unpredictable data flow, difficulty reasoning about system behavior.

## System-by-System Analysis

### 1. Entity Component System (ECS) - **Partially Good Design**

**Strengths:**
- Clean component separation (`Core/ECS/Components/`)
- Proper entity-component relationships
- ComponentManager provides efficient storage (`ComponentManager.cs:12-136`)
- EntityWorld provides good query interface (`EntityWorld.cs`)

**Weaknesses:**
- No entity archetype system completed yet
- Missing systems (logic components)
- ECS queries not used consistently throughout codebase
- Components still accessed through legacy entity properties

**Recommendations:**
- Complete ECS Phase 2 (Factory & Locale migration) as highest priority
- Implement proper archetype-based entity creation patterns
- Create system components for game logic (MovementSystem, CombatSystem)

### 2. Command System - **Needs Architectural Overhaul**

**Current Design Issues:**
- Static command registration makes testing difficult
- Command parsing is primitive (string prefix matching)
- No command queuing or priority system implemented
- Mixed responsibilities in CommandService (parsing + execution + registration)

**Recommended Design Patterns:**

```csharp
// Command Registry with dependency injection
public interface ICommandRegistry 
{
    void RegisterCommand<T>(string[] aliases) where T : ICommand;
    ICommand? ResolveCommand(string input);
}

// Command parser separated from registry
public interface ICommandParser 
{
    CommandParseResult Parse(string input);
}

// Command pipeline for middleware
public interface ICommandPipeline 
{
    Task<CommandResult> ExecuteAsync(ICommand command, CommandContext context);
}
```

**Benefits:**
- Testable command system
- Plugin-able command registration
- Middleware support (logging, validation, permissions)
- Async command execution for I/O operations

### 3. Combat System - **Functional but Inflexible**

**Current Issues:**
- Monolithic auto-attack processing (`CombatHandler.ProcessEntityAutoAttack:143-299`)
- No strategy pattern for different combat mechanics
- Hard-coded damage calculations
- Direct coupling to IO system

**Recommended Design:**

```csharp
public interface ICombatAction 
{
    Task<CombatResult> ExecuteAsync(ICombatContext context);
}

public interface ICombatSystem 
{
    void RegisterAction<T>(string actionName) where T : ICombatAction;
    Task ProcessCombatRoundAsync();
}

// Event-driven combat
public class CombatEvents 
{
    public event EventHandler<AttackEvent> AttackExecuted;
    public event EventHandler<DamageEvent> DamageDealt;
    public event EventHandler<DeathEvent> EntityDied;
}
```

**Benefits:**
- Extensible combat mechanics
- Easier testing of individual actions
- Event-driven damage/effect processing
- Support for varied combat scenarios from use cases

### 4. Input/Output System - **Adequate but Limited**

**Current Design:**
- Simple queue-based IOHandler works for basic scenarios
- No support for rich formatting or structured output
- Missing connection abstraction for different client types

**Recommended Improvements:**

```csharp
public interface IOutputFormatter 
{
    string FormatText(string text, FormatOptions options);
    string FormatCombatMessage(CombatEvent evt);
    string FormatMudMap(Room room, ViewOptions options);
}

public interface IConnectionManager 
{
    Task SendToClientAsync(string clientId, IOutputMessage message);
    Task SendToRoomAsync(uint roomId, IOutputMessage message);
    Task SendToAreaAsync(uint areaId, IOutputMessage message);
}
```

### 5. Data Access & Cache - **Good Foundation, Needs Events**

**Strengths:**
- Unified cache system works well (`DataCache.cs`)
- Clean separation of prototype vs instance objects
- Type-safe queries through generic methods

**Missing Features:**
- No change tracking for dirty objects
- Missing observer pattern for cache changes
- No event publishing when objects are modified

**Recommended Event Integration:**

```csharp
public interface ICacheEventPublisher 
{
    event EventHandler<EntityCreatedEvent> EntityCreated;
    event EventHandler<EntityModifiedEvent> EntityModified;
    event EventHandler<EntityDeletedEvent> EntityDeleted;
}
```

## Use Case Architectural Gaps

### Complex Scenarios Not Well Supported:

1. **Group Combat (Use Case: Group Combat Initiation)**
   - No group management system
   - Missing party/group entity relationships
   - Combat system assumes 1v1 scenarios

2. **Editor Operations (Use Cases: Area/Mob Deletion)**
   - No cascade delete handling
   - Missing transaction support for complex operations
   - No undo/redo system

3. **Dynamic Content (Use Case: Mob Wandering)**
   - No behavior system integration with ECS
   - Missing AI/automation framework
   - No scheduled task system

## Missing Architectural Components

### 1. Event Aggregator/Message Bus
**Purpose:** Decouple systems and enable reactive programming
**Example Implementation:**
```csharp
public interface IEventBus 
{
    Task PublishAsync<T>(T eventData) where T : class;
    IDisposable Subscribe<T>(Func<T, Task> handler) where T : class;
}
```

### 2. Plugin/Module System
**Purpose:** Enable feature extensions and maintainable growth
**Example Structure:**
```
Core/Modules/
  IGameModule.cs
  ModuleRegistry.cs
  ModuleLoader.cs
Modules/
  CombatModule/
  ShopModule/
  CraftingModule/
```

### 3. Behavior Tree System  
**Purpose:** Support complex NPC behaviors and game automation
**Integration:** Connect with ECS for dynamic behavior assignment

### 4. Query System
**Purpose:** Efficient spatial and logical queries for game world
```csharp
public interface IWorldQueries 
{
    IQueryable<T> EntitiesInRange<T>(Position center, float radius);
    IQueryable<T> EntitiesWithComponent<T, C>() where C : IComponent;
    IQueryable<Room> RoomsConnectedTo(uint roomId);
}
```

## Recommended Design Patterns & Guidelines

### 1. Consistent Event-Driven Architecture
- Use events for all cross-system communication
- Implement command/query segregation (CQRS) for read/write operations
- Establish clear event naming conventions

### 2. Plugin-Based Module System
```
Core/          # Core engine, ECS, basic systems
Modules/       # Game feature modules (combat, crafting, etc.)
Plugins/       # Third-party or optional features
```

### 3. Clean Code Guidelines for MUD Development

**Entity Creation Pattern:**
```csharp
// Use factories consistently
var player = EntityFactory.CreateEntity(EntityArchetype.Player)
    .WithComponent<TransformComponent>(room.Instance)
    .WithComponent<InventoryComponent>(50) // capacity
    .Build();
```

**Command Development Pattern:**
```csharp
[Command("look", "l")]
[RequiredState(EntityState.Active)]
public class LookCommand : ICommand 
{
    public async Task<CommandResult> ExecuteAsync(CommandContext context)
    {
        // Implementation with proper error handling
    }
}
```

**Event-Driven Combat Action:**
```csharp
public class AttackAction : ICombatAction 
{
    public async Task<CombatResult> ExecuteAsync(ICombatContext context) 
    {
        // Calculate attack
        var result = CalculateAttackResult(context);
        
        // Publish events
        await _eventBus.PublishAsync(new AttackExecutedEvent(result));
        
        return result;
    }
}
```

### 4. Testing Strategy
- Unit tests for individual components and systems
- Integration tests for cross-system interactions
- Behavior-driven tests for complex use cases
- Property-based testing for game mechanics

## Implementation Roadmap

### Phase 1: Complete ECS Migration (Current Priority)
1. Finish ECS Phase 1.5 - Entity hierarchy flattening
2. Complete ECS Phase 2 - Factory & Locale migration  
3. Create proper archetype system with validation

### Phase 2: Event System Foundation
1. Implement event bus/aggregator
2. Convert combat system to event-driven model
3. Add event-driven persistence with dirty tracking

### Phase 3: Command System Overhaul
1. Implement command registry with DI
2. Add command pipeline with middleware
3. Create command templating system

### Phase 4: Advanced Systems
1. Implement behavior tree system for NPCs
2. Create spatial query system
3. Add plugin/module architecture

### Phase 5: Performance & Scalability
1. Optimize ECS component storage
2. Implement object pooling for high-frequency objects
3. Add performance monitoring and metrics

## Conclusion

The Hedron codebase has a solid foundation but needs architectural consistency and completion of the ECS migration to reach its full potential.
The missing event system and inconsistent design patterns are the primary barriers to clean, maintainable code.

**Immediate Action Items:**
1. **Complete ECS Phase 2** - This will provide the consistent foundation needed
2. **Design and implement event bus** - This will enable proper decoupling
3. **Establish coding guidelines** - Define patterns for common MUD operations
4. **Create architectural templates** - Examples for implementing new commands, combat actions, etc.

With these changes, the codebase will support complex MUD scenarios while maintaining clean separation of concerns and testability.