using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using HtmlAgilityPack;

public class LinkVertice
{
    public string Href { get; set; }
    public string Text { get; set; }

    public LinkVertice(string href, string text)
    {
        Href = href;
        Text = text;
    }

    public override bool Equals(object obj)
    {
        return obj is LinkVertice vertice && Href == vertice.Href;
    }

    public override int GetHashCode()
    {
        return Href.GetHashCode();
    }

    public override string ToString()
    {
        return $"{Text} ({Href})";
    }
}

public class Graph
{
    private Dictionary<LinkVertice, List<(LinkVertice, int)>> adjList = new Dictionary<LinkVertice, List<(LinkVertice, int)>>();

    public void AddEdge(LinkVertice from, LinkVertice to, int weight)
    {
        if (!adjList.ContainsKey(from))
        {
            adjList[from] = new List<(LinkVertice, int)>();
        }

        if (!adjList.ContainsKey(to))
        {
            adjList[to] = new List<(LinkVertice, int)>();
        }

        adjList[from].Add((to, weight));
    }

    public async Task DijkstraAsync(LinkVertice start, LinkVertice destination)
    {
        var distances = new Dictionary<LinkVertice, int>();
        var previous = new Dictionary<LinkVertice, LinkVertice>();
        var priorityQueue = new PriorityQueue<LinkVertice, int>();

        foreach (var node in adjList.Keys)
        {
            distances[node] = int.MaxValue;
            previous[node] = null;
            priorityQueue.Enqueue(node, int.MaxValue);
        }
        distances[start] = 0;
        priorityQueue.Enqueue(start, 0);

        while (priorityQueue.Count > 0)
        {
            var current = priorityQueue.Dequeue();

            if (distances[current] == int.MaxValue)
                continue;

            foreach (var (neighbor, weight) in adjList[current])
            {
                int distance = distances[current] + weight;

                if (distance < distances[neighbor])
                {
                    distances[neighbor] = distance;
                    previous[neighbor] = current;
                    priorityQueue.Enqueue(neighbor, distance);
                }
            }
        }

        // Check if the destination is reachable
        if (distances.ContainsKey(destination) && distances[destination] < int.MaxValue)
        {
            Console.WriteLine($"Path to {destination}: ");
            PrintPath(previous, destination);
            Console.WriteLine();
        }
        else
        {
            Console.WriteLine($"No path to {destination}.");
        }
    }

    private void PrintPath(Dictionary<LinkVertice, LinkVertice> previous, LinkVertice node)
    {
        if (previous[node] == null)
        {
            Console.Write(node);
            return;
        }
        PrintPath(previous, previous[node]);
        Console.Write(" -> " + node);
    }
}

public class Program
{
    private static readonly HttpClient Client = new HttpClient(
        new HttpClientHandler()
        {
            Proxy = new WebProxy()
            {
                Address = new Uri("http://rb-proxy-ca1.bosch.com:8080"),
                Credentials = new NetworkCredential("disrct", "etsps2024401")
            }
        }
    );

    private static readonly SemaphoreSlim Semaphore = new SemaphoreSlim(10); // Limit to 10 concurrent tasks

    private static async Task<string> GetHTMLAsync(string url)
    {
        await Semaphore.WaitAsync();
        try
        {
            return await Client.GetStringAsync(url);
        }
        finally
        {
            Semaphore.Release();
        }
    }

    private static List<LinkVertice> GetValues(string rawHTML, string baseUrl)
    {
        var html = new HtmlDocument();
        html.LoadHtml(rawHTML);

        var urls = html.DocumentNode.SelectNodes("//*[@id=\"content\"]//a[@href]")
            .Where(x => x.Attributes["href"].Value.StartsWith("/wiki"))
            .Select(x => 
                new LinkVertice(
                    new Uri(new Uri(baseUrl), x.Attributes["href"].Value)
                    .AbsoluteUri.Trim(), x.InnerText.Trim()
                ));

        return urls.ToList();
    }

    public static async Task Main(string[] args)
    {
        Console.Write("Start: ");
        var startUrl = Console.ReadLine();

        Console.Write("Destination: ");
        var destinationUrl = Console.ReadLine();

        var startLink = new LinkVertice(startUrl, "Start Page");
        var destinationLink = new LinkVertice(destinationUrl, "Destination Page");

        var graph = new Graph();
        var queue = new Queue<LinkVertice>();
        var visited = new HashSet<LinkVertice>();
        var found = false;

        queue.Enqueue(startLink);
        visited.Add(startLink);

        while (queue.Count > 0)
        {
            var currentBatch = new List<Task>();

            while (queue.Count > 0 && currentBatch.Count < 25) // Process up to 10 URLs at a time
            {
                var current = queue.Dequeue();
                currentBatch.Add(Task.Run(async () =>
                {

                    Console.WriteLine($"> {current.Text}");

                    String rawHTML;
                    try {
                        rawHTML = (await GetHTMLAsync(current.Href)).Trim();;
                    } catch {
                        return;
                    }

                    var neighbors = GetValues(rawHTML, current.Href.Trim());

                    lock (visited)
                    {
                        foreach (var neighbor in neighbors)
                        {
                            if (!visited.Contains(neighbor))
                            {
                                graph.AddEdge(current, neighbor, 1); // Assuming a default weight of 1 for simplicity
                                queue.Enqueue(neighbor);
                                visited.Add(neighbor);
                            }
                        }
                    }
                }));
            }

            await Task.WhenAll(currentBatch);

            // Check if destination is reached within this batch
            if (visited.Contains(destinationLink))
            {
                found = true;
                break;
            }
        }

        if (found)
        {
            await graph.DijkstraAsync(startLink, destinationLink);
        }
        else
        {
            Console.WriteLine("Destination not found.");
        }
    }
}
