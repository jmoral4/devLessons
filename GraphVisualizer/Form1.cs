using System.Xml.Linq;

namespace GraphVisualizer
{
    public partial class Form1 : Form
    {
        private BufferedGraphics bufferedGraphics;
        private BufferedGraphicsContext context;
        private Graphics graphics;

        private Queue<Node> renderQueue = new Queue<Node>(); // the queue of all objects to be drawn

        int nodeWidth = 32; // dimensions of the node being drawn on screen
        int nodeHeight = 32;

        Graph graph; // our graph
        Node endNode; //target node 
        Node startNode; //starting node

        enum Mode { BFS, DFS, AStar }
        Mode RunMode; // the current running graph algorithm being used (used for cancelling in-progress runs)
        
        bool isDrawn = false; // used to control the one-time background painting used in OnPaint

        public Form1()
        {
            InitializeComponent();
            context = BufferedGraphicsManager.Current;
            graphics = this.CreateGraphics();
            bufferedGraphics = context.Allocate(graphics, this.ClientRectangle);

            renderTimer.Interval = 25; // the time between frames in milliseconds. Smaller is faster, larger is slower. It has a lower practical limit though of around 25ms. 
            renderTimer.Start();
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            // generate an initial grid when the form is loaded
             RandomizeGrid();            
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            // We need to wait for the form to be fully loaded to draw on it.
            // There is no built-in event handler for this so we use OnPaint and a flag to ensure we only paint the grid once
            base.OnPaint(e);

            if (!isDrawn)
            {
                DrawInitialGrid();
                isDrawn = true;
            }
        }
      
        public void DrawInitialGrid()
        {
            // set the background color by using the Clear to Color method
            bufferedGraphics.Graphics.Clear(Color.White);
            foreach (var node in graph.Nodes)
            {
                Color color = Color.Gray;
                if (node.IsEndNode)
                {
                    color = Color.Yellow;
                }

                if (node.IsStartNode)
                {
                    color = Color.Blue;
                }
                if (node.NodeState == Node.State.Blocked)
                {
                    color = Color.DarkRed;
                }

                // add rectangles representing each node to the graphics buffer
                bufferedGraphics.Graphics.FillRectangle(new SolidBrush(color),
                    new Rectangle(node.X, node.Y, nodeWidth, nodeHeight));
            }

            // once everything is in the buffer, render it
            bufferedGraphics.Render();
        }

        // Reset the graph but keep the initial state of eaach node
        public void Reset()
        {
            // clear our list of visited nodes and the render queue
            visitedNodes.Clear();
            renderQueue.Clear();
            
            //clear the state of each node in the graph
            foreach (var node in graph.Nodes)
            {
                node.Parent = null; // reset the parent used for tracing the path
                node.GCost = double.MaxValue; //reset the cost for all AStar purposes


                if (node.NodeState == Node.State.Visited)  //reset the visited state
                    node.NodeState = Node.State.Unvisited;

                node.Color = Color.Gray; //reset the color
            }

            RenderOnce(graph.Nodes[0]); // add this reset state to the render queue
            bufferedGraphics.Render();  // render it
        }

        // Generate the initial grid
        public void RandomizeGrid()
        {
            int gridSize = 20; //dimensions of the grid, since we only have one value it will always be a square, create seperate x/y sizes if you want rectangles
            int nodeSize = nodeHeight; // we're only using the height here to define a 'size', making them squares, 
            graph = new Graph();
            graph.Nodes = new List<Node>();
            Random r = new Random();

            for (int x = 0; x < gridSize; x++)
            {
                for (int y = 0; y < gridSize; y++)
                {
                    Node node;
                    if (r.Next(100) > 75)
                    {
                        node = new Node
                        {
                            Color = Color.DarkRed,
                            AdjacentNodes = new List<Node>(),
                            X = x * nodeSize,
                            Y = y * nodeSize,
                            NodeState = Node.State.Blocked
                        };
                    }
                    else
                    {
                        node = new Node
                        {
                            Color = Color.White,
                            AdjacentNodes = new List<Node>(),
                            X = x * nodeSize,
                            Y = y * nodeSize
                        };
                    }
                    node.GCost = double.MaxValue;
                    graph.Nodes.Add(node);
                }
            }

            //connect the nodes
            for (int x = 0; x < gridSize; x++)
            {
                for (int y = 0; y < gridSize; y++)
                {
                    Node currentNode = graph.Nodes[y * gridSize + x];
                    // Connect to the right neighbor
                    if (x < gridSize - 1) currentNode.AdjacentNodes.Add(graph.Nodes[y * gridSize + (x + 1)]);
                    // Connect to the left neighbor
                    if (x > 0) currentNode.AdjacentNodes.Add(graph.Nodes[y * gridSize + (x - 1)]);
                    // Connect to the top neighbor
                    if (y > 0) currentNode.AdjacentNodes.Add(graph.Nodes[(y - 1) * gridSize + x]);
                    // Connect to the bottom neighbor
                    if (y < gridSize - 1) currentNode.AdjacentNodes.Add(graph.Nodes[(y + 1) * gridSize + x]);
                }
            }

            endNode = graph.Nodes[graph.Nodes.Count - 1];
            endNode.IsEndNode = true;
            endNode.NodeState = Node.State.Unvisited;
            startNode = graph.Nodes[0];
            startNode.NodeState = Node.State.Unvisited;
            startNode.GCost = 0;
            startNode.IsStartNode = true;
        }



        public async Task BFS(Node startNode)
        {
            if (RunMode != Mode.BFS)
                return;
            Queue<Node> queue = new Queue<Node>();

            startNode.NodeState = Node.State.Current;
            queue.Enqueue(startNode);
            renderQueue.Enqueue(startNode);

            while (queue.Count > 0)
            {
                if (RunMode != Mode.BFS)
                    return;
                Node currentNode = queue.Dequeue();
                currentNode.NodeState = Node.State.Visited;
                renderQueue.Enqueue(currentNode);

                foreach (Node adjacentNode in currentNode.AdjacentNodes)
                {
                    // Only process unvisited nodes that are not blocked  
                    if (adjacentNode.NodeState == Node.State.Unvisited && adjacentNode.NodeState != Node.State.Blocked)
                    {
                        adjacentNode.Parent = currentNode;
                        adjacentNode.NodeState = Node.State.Current;
                        queue.Enqueue(adjacentNode);
                        renderQueue.Enqueue(adjacentNode);
                    }

                }

                // await Task.Delay(50);
            }
        }

        public async Task DFS(Node startNode)
        {
            if (RunMode != Mode.DFS)
                return;
            Stack<Node> queue = new Stack<Node>();

            startNode.NodeState = Node.State.Current;
            queue.Push(startNode);
            renderQueue.Enqueue(startNode);

            while (queue.Count > 0)
            {
                if (RunMode != Mode.DFS)
                    return;
                Node currentNode = queue.Pop();
                currentNode.NodeState = Node.State.Visited;
                renderQueue.Enqueue(currentNode);

                foreach (Node adjacentNode in currentNode.AdjacentNodes)
                {
                    // Only process unvisited nodes that are not blocked  
                    if (adjacentNode.NodeState == Node.State.Unvisited && adjacentNode.NodeState != Node.State.Blocked)
                    {
                        adjacentNode.Parent = currentNode;
                        adjacentNode.NodeState = Node.State.Current;
                        queue.Push(adjacentNode);
                        renderQueue.Enqueue(adjacentNode);
                    }

                }

            }
        }

        public List<Node> FindPath(Node startNode, Node endNode)
        {
            List<Node> path = new List<Node>();
            Node currentNode = endNode;

            while (currentNode != null)
            {
                path.Add(currentNode);
                currentNode = currentNode.Parent;
            }

            // The path is from end to start, so we reverse it
            path.Reverse();

            return path;
        }




        private HashSet<Node> visitedNodes = new HashSet<Node>();
        private void RenderOnce(Node n)
        {

            if (visitedNodes.Contains(n))
            {
                return; // Already visited this node, so skip it  
            }

            visitedNodes.Add(n);

            Color color = Color.Gray;
            var nodeToRender = n;
            if (nodeToRender.IsEndNode)
            {
                color = Color.Yellow;
            }

            if (nodeToRender.IsStartNode)
            {
                color = Color.Blue;
            }
            if (nodeToRender.NodeState == Node.State.Blocked)
            {
                color = Color.DarkRed;
            }

            bufferedGraphics.Graphics.FillRectangle(new SolidBrush(color),
                new Rectangle(nodeToRender.X, nodeToRender.Y, nodeWidth, nodeHeight));



            foreach (var node in n.AdjacentNodes)
            {
                RenderOnce(node);
            }
        }

        private void renderTimer_Tick(object sender, EventArgs e)
        {
            // Clear the buffer  
            //

            if (renderQueue.Count > 0)
            {
                Node nodeToRender = renderQueue.Dequeue();

                // Determine the color based on the node's state  
                Color color = Color.Gray;
                if (nodeToRender.NodeState == Node.State.Visited)
                {
                    color = Color.Black;
                }
                else if (nodeToRender.NodeState == Node.State.Current)
                {
                    color = Color.Red;
                }


                // Render the node with the appropriate color  
                bufferedGraphics.Graphics.FillRectangle(new SolidBrush(color),
                    new Rectangle(nodeToRender.X, nodeToRender.Y, nodeWidth, nodeHeight));

                if (nodeToRender.IsEndNode)
                {
                    var path = FindPath(startNode, endNode);
                    if (path != null)
                    {
                        color = Color.Yellow;
                        foreach (var node in path)
                        {
                            bufferedGraphics.Graphics.FillRectangle(new SolidBrush(color),
                                    new Rectangle(node.X, node.Y, nodeWidth, nodeHeight));
                        }

                    }

                }


                // Render the buffer to the screen  
                bufferedGraphics.Render();
            }
        }

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            bufferedGraphics.Dispose();
            graphics.Dispose();
        }

        public async Task AStar(Node startNode)
        {
            if (RunMode != Mode.AStar)
                return;
            List<Node> openSet = new List<Node>();
            HashSet<Node> closedSet = new HashSet<Node>();

            startNode.NodeState = Node.State.Current;
            openSet.Add(startNode);
            renderQueue.Enqueue(startNode);

            while (openSet.Count > 0)
            {
                if (RunMode != Mode.AStar)
                    return;
                Node currentNode = openSet.OrderBy(x => x.FCost).First();
                openSet.Remove(currentNode);
                closedSet.Add(currentNode);

                currentNode.NodeState = Node.State.Visited;
                renderQueue.Enqueue(currentNode);

                if (currentNode == endNode)
                {
                    // Found the goal
                    return;
                }

                foreach (Node adjacentNode in currentNode.AdjacentNodes)
                {
                    if (closedSet.Contains(adjacentNode) || adjacentNode.NodeState == Node.State.Blocked)
                    {
                        continue;
                    }

                    double tentativeGCost = currentNode.GCost + GetDistance(currentNode, adjacentNode);
                    if (tentativeGCost < adjacentNode.GCost || !openSet.Contains(adjacentNode))
                    {
                        adjacentNode.GCost = tentativeGCost;
                        adjacentNode.HCost = GetDistance(adjacentNode, endNode);
                        adjacentNode.Parent = currentNode;

                        if (!openSet.Contains(adjacentNode))
                        {
                            openSet.Add(adjacentNode);
                            adjacentNode.NodeState = Node.State.Current;
                            renderQueue.Enqueue(adjacentNode);
                        }
                    }
                }

                // await Task.Delay(50);
            }
        }

        public double GetDistance(Node a, Node b)
        {
            // You can use Euclidean distance, Manhattan distance, or any other distance metric appropriate for your grid
            // For a simple square grid, Manhattan distance works well:
            return Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y);
        }

        private void button1_Click(object sender, EventArgs e)
        {
            Reset();
            RunMode = Mode.BFS;
            BFS(graph.Nodes[0]);
        }

        private void btnDFS_Click(object sender, EventArgs e)
        {
            Reset();
            RunMode = Mode.DFS;
            DFS(graph.Nodes[0]);
        }

        private void btnAStar_Click(object sender, EventArgs e)
        {
            Reset();
            RunMode = Mode.AStar;
            AStar(graph.Nodes[0]);
        }

        private void btnRegen_Click(object sender, EventArgs e)
        {
            RandomizeGrid();
            Reset();

        }
    }


    public class Graph
    {
        public List<Node> Nodes { get; set; }        
    }

}