namespace GraphExplore
{
    internal class Program
    {
        static void Main(string[] args)
        {
/*
             
Let's consider this graph:
        1  
       / \  
      2   3  
         / \  
        4   5  
       / \  
      6   7                                                  
     /  
    8   


  */

            Node node1 = new Node(1);
            Node node2 = new Node(2);
            Node node3 = new Node(3);
            Node node4 = new Node(4);
            Node node5 = new Node(5);
            Node node6 = new Node(6);
            Node node7 = new Node(7);
            Node node8 = new Node(8);            

            node1.Neighbors.Add(node2);
            node1.Neighbors.Add(node3);
            node3.Neighbors.Add(node4);
            node3.Neighbors.Add(node5);
            node4.Neighbors.Add(node6);
            node4.Neighbors.Add(node7);
            node6.Neighbors.Add(node8);            

            Graph graph = new Graph();

            // Test DFS  
            Console.WriteLine("DFS");
            graph.DFS(node1);  // Output: 1 2 3 4 6 8 7 5  
            Console.WriteLine();
            // Reset visited flags  
            node1.Visited = false;
            node2.Visited = false;
            node3.Visited = false;
            node4.Visited = false;
            node5.Visited = false;
            node6.Visited = false;
            node7.Visited = false;
            node8.Visited = false;
            

            // Test BFS  
            Console.WriteLine("BFS");
            graph.BFS(node1);  // Output: 1 2 3 4 5 6 7 8
        }
    }

    public class Node
    {
        public int Value;
        public List<Node> Neighbors;
        public bool Visited;

        public Node(int value)
        {
            Value = value;
            Neighbors = new List<Node>();
        }
    }

    public class Graph
    {
        public void DFS(Node node)
        {
            if (node == null || node.Visited)
                return;

            Console.Write(node.Value + " ");
            node.Visited = true;

            foreach (Node neighbor in node.Neighbors)
            {
                DFS(neighbor);
            }
        }

        public void BFS(Node node)
        {
            Queue<Node> queue = new Queue<Node>();
            node.Visited = true;
            queue.Enqueue(node);

            while (queue.Count > 0)
            {
                node = queue.Dequeue();
                Console.Write(node.Value + " ");

                foreach (Node neighbor in node.Neighbors)
                {
                    if (!neighbor.Visited)
                    {
                        queue.Enqueue(neighbor);
                        neighbor.Visited = true;
                    }
                }
            }
        }
    }
}