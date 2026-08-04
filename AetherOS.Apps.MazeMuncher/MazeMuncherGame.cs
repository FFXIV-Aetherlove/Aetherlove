using System;
using System.Collections.Generic;
using System.Numerics;

namespace AetherOS.Apps.MazeMuncher;

public enum GhostState
{
    House,
    Leaving,
    Normal,
    Frightened,
    Eyes,
}

/// <summary>Maze Muncher: eat every dot without being caught, and turn the tables for a few seconds on a
/// power pellet. Movement is continuous along tile lanes, with turns only accepted near a tile centre,
/// which is what gives the original its forgiving cornering.</summary>
public sealed class MazeMuncherGame
{
    public const int Columns = 15;
    public const int Rows = 18;

    private const float TurnSlack = 0.28f;
    private const float PlayerSpeed = 6.2f;
    private const float EyesSpeed = 10f;
    private const int DotPoints = 10;
    private const int PelletPoints = 50;
    private const int LevelClearBonus = 500;
    private const int StartLives = 3;
    private const float DeathPause = 1.4f;
    private const float ReadyPause = 1.6f;

    private static readonly Vector2 HouseCentre = new(7f, 9f);

    /// <summary>The corridor tile directly above the pen door. A ghost heading for the player would never
    /// pick the door on its own (going up increases the distance), so leaving the pen is its own state with
    /// this as its target.</summary>
    private static readonly Vector2 DoorExit = new(7f, 7f);
    private static readonly Vector2[] Directions =
        [new(0f, -1f), new(-1f, 0f), new(0f, 1f), new(1f, 0f)];

    /// <summary>Corners the shy ghost retreats to, and where the ambusher aims when it has no lead.</summary>
    private static readonly Vector2[] ScatterCorners =
        [new(1f, 1f), new(13f, 1f), new(1f, 16f), new(13f, 16f)];

    private static readonly string[] Layout =
    [
        "###############",
        "#......#......#",
        "#o##.#.#.#.##o#",
        "#.............#",
        "#.##.##.##.##.#",
        "#.............#",
        "####.##.##.####",
        "   ..#...#..   ",
        "###..##-##..###",
        "###..#GGG#..###",
        "###..#####..###",
        "#......#......#",
        "#.##.##.##.##.#",
        "#......P......#",
        "#.#.###.###.#.#",
        "#o...#...#...o#",
        "#....#.#.#....#",
        "###############",
    ];

    public sealed class Ghost
    {
        public Vector2 Pos;
        public Vector2 Dir;
        public GhostState State;
        public int Personality;
        public float ReleaseTimer;

        /// <summary>The tile this ghost last picked a direction on, so it picks once per tile.</summary>
        public Vector2 DecidedAt = new(-1f, -1f);
    }

    private readonly Random random = new();
    private readonly char[,] tiles = new char[Columns, Rows];
    private readonly List<Ghost> ghosts = [];

    private Vector2 playerStart;
    private Vector2 queuedDir;
    private float frightTimer;
    private float deathTimer;
    private float readyTimer;
    private int ghostChain;

    public int Score { get; private set; }

    public int Level { get; private set; }

    public int Lives { get; private set; }

    public bool Dead { get; private set; }

    public int DotsLeft { get; private set; }

    public Vector2 PlayerPos { get; private set; }

    public Vector2 PlayerDir { get; private set; }

    public IReadOnlyList<Ghost> Ghosts => this.ghosts;

    /// <summary>True while the round is frozen: after a death, or during the short "ready" beat.</summary>
    public bool Frozen => this.deathTimer > 0f || this.readyTimer > 0f;

    public bool Dying => this.deathTimer > 0f;

    public char TileAt(int x, int y) => this.tiles[x, y];

    public static bool IsWall(char tile) => tile == '#';

    public void Reset()
    {
        this.Score = 0;
        this.Level = 0;
        this.Lives = StartLives;
        this.Dead = false;
        StartLevel();
    }

    private void StartLevel()
    {
        this.Level++;
        this.DotsLeft = 0;
        for (var y = 0; y < Rows; y++)
        {
            for (var x = 0; x < Columns; x++)
            {
                var tile = Layout[y][x];
                if (tile == 'P')
                {
                    this.playerStart = new Vector2(x, y);
                    tile = '.';
                }
                else if (tile == 'G')
                {
                    tile = ' ';
                }
                this.tiles[x, y] = tile;
                if (tile is '.' or 'o')
                {
                    this.DotsLeft++;
                }
            }
        }
        PlaceActors();
    }

    private void PlaceActors()
    {
        this.PlayerPos = this.playerStart;
        this.PlayerDir = Vector2.Zero;
        this.queuedDir = Vector2.Zero;
        this.frightTimer = 0f;
        this.ghostChain = 0;
        this.readyTimer = ReadyPause;
        this.deathTimer = 0f;

        this.ghosts.Clear();
        for (var i = 0; i < 4; i++)
        {
            // The chaser starts outside so there is a hunt from the first second; the rest file out on a stagger.
            var penned = i > 0;
            this.ghosts.Add(new Ghost
            {
                Pos = penned ? new Vector2(5f + i, 9f) : DoorExit,
                Dir = Directions[1],
                State = penned ? GhostState.House : GhostState.Normal,
                Personality = i,
                ReleaseTimer = i * 3f,
            });
        }
    }

    public void Turn(Vector2 direction) => this.queuedDir = direction;

    /// <summary>The pen door is passable only by a ghost that is on its way out or back home, so a hunting
    /// ghost can never wander into the pen and get stuck there.</summary>
    private bool Walkable(int x, int y, bool forGhost, bool useDoor = false)
    {
        if (y < 0 || y >= Rows)
        {
            return false;
        }
        x = ((x % Columns) + Columns) % Columns;
        var tile = this.tiles[x, y];
        if (tile == '#')
        {
            return false;
        }
        return tile != '-' || (forGhost && useDoor);
    }

    private static Vector2 Wrap(Vector2 pos)
    {
        if (pos.X < -0.5f)
        {
            pos.X += Columns;
        }
        else if (pos.X > Columns - 0.5f)
        {
            pos.X -= Columns;
        }
        return pos;
    }

    private static bool NearCentre(Vector2 pos) =>
        Math.Abs(pos.X - MathF.Round(pos.X)) < TurnSlack && Math.Abs(pos.Y - MathF.Round(pos.Y)) < TurnSlack;

    public void Tick(float delta)
    {
        if (this.Dead)
        {
            return;
        }
        if (this.readyTimer > 0f)
        {
            this.readyTimer -= delta;
            return;
        }
        if (this.deathTimer > 0f)
        {
            this.deathTimer -= delta;
            if (this.deathTimer <= 0f)
            {
                if (this.Lives <= 0)
                {
                    this.Dead = true;
                }
                else
                {
                    PlaceActors();
                }
            }
            return;
        }

        if (this.frightTimer > 0f)
        {
            this.frightTimer -= delta;
            if (this.frightTimer <= 0f)
            {
                foreach (var ghost in this.ghosts)
                {
                    if (ghost.State == GhostState.Frightened)
                    {
                        ghost.State = GhostState.Normal;
                    }
                }
                this.ghostChain = 0;
            }
        }

        // Turns are only accepted near a tile centre, so a single long frame could carry an actor clean
        // past a junction (or through a wall). Substepping keeps every move shorter than that window.
        var steps = Math.Clamp((int)Math.Ceiling(delta / 0.016f), 1, 16);
        var step = delta / steps;
        for (var i = 0; i < steps; i++)
        {
            MovePlayer(step);
            Consume();
            foreach (var ghost in this.ghosts)
            {
                MoveGhost(ghost, step);
            }
            CheckCollisions();
            if (this.deathTimer > 0f)
            {
                return;
            }
        }

        if (this.DotsLeft == 0)
        {
            this.Score += LevelClearBonus;
            StartLevel();
        }
    }

    private void MovePlayer(float delta)
    {
        var pos = this.PlayerPos;
        var tileX = (int)MathF.Round(pos.X);
        var tileY = (int)MathF.Round(pos.Y);

        if (this.queuedDir != Vector2.Zero && NearCentre(pos)
            && Walkable(tileX + (int)this.queuedDir.X, tileY + (int)this.queuedDir.Y, forGhost: false))
        {
            // Snapping to the lane centre on a turn is what stops diagonal drift over a long run.
            pos = new Vector2(tileX, tileY);
            this.PlayerDir = this.queuedDir;
            this.queuedDir = Vector2.Zero;
        }

        if (this.PlayerDir == Vector2.Zero)
        {
            this.PlayerPos = pos;
            return;
        }

        var ahead = Walkable(tileX + (int)this.PlayerDir.X, tileY + (int)this.PlayerDir.Y, forGhost: false);
        if (!ahead && NearCentre(pos))
        {
            this.PlayerPos = new Vector2(tileX, tileY);
            return;
        }
        this.PlayerPos = Wrap(pos + (this.PlayerDir * PlayerSpeed * delta));
    }

    private void Consume()
    {
        var x = (int)MathF.Round(this.PlayerPos.X);
        var y = (int)MathF.Round(this.PlayerPos.Y);
        if (x < 0 || x >= Columns || y < 0 || y >= Rows)
        {
            return;
        }
        var tile = this.tiles[x, y];
        if (tile == '.')
        {
            this.tiles[x, y] = ' ';
            this.DotsLeft--;
            this.Score += DotPoints;
        }
        else if (tile == 'o')
        {
            this.tiles[x, y] = ' ';
            this.DotsLeft--;
            this.Score += PelletPoints;
            this.frightTimer = Math.Max(2f, 7f - (this.Level * 0.5f));
            this.ghostChain = 0;
            foreach (var ghost in this.ghosts)
            {
                if (ghost.State == GhostState.Normal)
                {
                    ghost.State = GhostState.Frightened;
                    ghost.Dir = -ghost.Dir;
                }
            }
        }
    }

    /// <summary>A hunting ghost stays just under the muncher and only creeps up with the level, so you can
    /// always outrun one in a straight line and losing is about being cornered, not outpaced.</summary>
    private float GhostSpeed(Ghost ghost) => ghost.State switch
    {
        GhostState.Eyes => EyesSpeed,
        GhostState.Frightened => 3f,
        GhostState.House => 2.5f,
        GhostState.Leaving => 4f,
        _ => Math.Min(PlayerSpeed * 0.98f, 4.6f + (this.Level * 0.2f)),
    };

    private void MoveGhost(Ghost ghost, float delta)
    {
        if (ghost.State == GhostState.House)
        {
            ghost.ReleaseTimer -= delta;
            if (ghost.ReleaseTimer <= 0f)
            {
                ghost.State = GhostState.Leaving;
            }
        }

        // Once per tile, and only once the ghost has actually REACHED that tile's centre. Deciding early (while
        // merely near it) and snapping would hand the ghost the remaining fraction of every tile for free,
        // which silently runs it far above its configured speed.
        var tile = new Vector2(MathF.Round(ghost.Pos.X), MathF.Round(ghost.Pos.Y));
        var reached = ghost.Dir == Vector2.Zero || Vector2.Dot(ghost.Pos - tile, ghost.Dir) >= 0f;
        if (reached && (tile != ghost.DecidedAt || ghost.Dir == Vector2.Zero))
        {
            ghost.DecidedAt = tile;
            // Re-centre the lane it is crossing but keep the overshoot along its heading, so no distance is
            // invented; a genuine turn still snaps fully, or it would corner through a wall.
            ghost.Pos = ghost.Dir.X != 0f ? new Vector2(ghost.Pos.X, tile.Y)
                : ghost.Dir.Y != 0f ? new Vector2(tile.X, ghost.Pos.Y)
                : tile;

            // State first, then steer: the new state's target has to apply on this very decision.
            if (ghost.State == GhostState.Leaving && tile.Y <= DoorExit.Y)
            {
                ghost.State = this.frightTimer > 0f ? GhostState.Frightened : GhostState.Normal;
            }
            else if (ghost.State == GhostState.Eyes && Vector2.Distance(tile, HouseCentre) < 0.6f)
            {
                ghost.State = GhostState.House;
                ghost.ReleaseTimer = 1.5f;
            }

            var steered = ChooseDirection(ghost);
            if (steered != ghost.Dir)
            {
                ghost.Pos = tile;
            }
            ghost.Dir = steered;
        }
        ghost.Pos = Wrap(ghost.Pos + (ghost.Dir * GhostSpeed(ghost) * delta));
    }

    private Vector2 ChooseDirection(Ghost ghost)
    {
        var tileX = (int)MathF.Round(ghost.Pos.X);
        var tileY = (int)MathF.Round(ghost.Pos.Y);
        var target = TargetFor(ghost);
        var reverse = -ghost.Dir;
        var useDoor = ghost.State is GhostState.Leaving or GhostState.Eyes;

        Vector2 best = Vector2.Zero;
        var bestScore = float.MaxValue;
        var options = new List<Vector2>();
        foreach (var dir in Directions)
        {
            if (!Walkable(tileX + (int)dir.X, tileY + (int)dir.Y, forGhost: true, useDoor))
            {
                continue;
            }
            if (dir == reverse && ghost.Dir != Vector2.Zero)
            {
                continue;
            }
            options.Add(dir);
            var next = new Vector2(tileX + dir.X, tileY + dir.Y);
            var score = Vector2.DistanceSquared(next, target);
            if (score < bestScore)
            {
                bestScore = score;
                best = dir;
            }
        }

        if (options.Count == 0)
        {
            return Walkable(tileX + (int)reverse.X, tileY + (int)reverse.Y, forGhost: true, useDoor)
                ? reverse
                : Vector2.Zero;
        }
        // Frightened ghosts and the wanderer pick at random; everyone else walks the shortest line.
        if (ghost.State == GhostState.Frightened || (ghost.Personality == 2 && ghost.State == GhostState.Normal))
        {
            return options[this.random.Next(options.Count)];
        }
        return best;
    }

    private Vector2 TargetFor(Ghost ghost)
    {
        if (ghost.State is GhostState.Eyes or GhostState.House)
        {
            return HouseCentre;
        }
        if (ghost.State == GhostState.Leaving)
        {
            return DoorExit;
        }
        return ghost.Personality switch
        {
            0 => this.PlayerPos,
            1 => this.PlayerPos + (this.PlayerDir * 4f),
            3 => Vector2.Distance(ghost.Pos, this.PlayerPos) > 8f ? this.PlayerPos : ScatterCorners[2],
            _ => this.PlayerPos,
        };
    }

    private void CheckCollisions()
    {
        foreach (var ghost in this.ghosts)
        {
            if (ghost.State is GhostState.Eyes or GhostState.House)
            {
                continue;
            }
            if (Vector2.Distance(ghost.Pos, this.PlayerPos) > 0.7f)
            {
                continue;
            }
            if (ghost.State == GhostState.Frightened)
            {
                ghost.State = GhostState.Eyes;
                this.ghostChain = Math.Min(this.ghostChain + 1, 4);
                this.Score += 100 * (int)Math.Pow(2, this.ghostChain);
                continue;
            }
            this.Lives--;
            this.deathTimer = DeathPause;
            return;
        }
    }
}
