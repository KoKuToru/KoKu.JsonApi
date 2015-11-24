simple JsonApi.org Serializer/Deserializer for C#
------

model

```c#
[KoKu.JsonApi.Type("post")]
public class Post {
    public int Id  {get; set;}
    public string Message {get; set;}
}

[KoKu.JsonApi.Type("user")]
public class User {
    public int Id {get; set;}
    
	public string Name {get; set;}
	public string Address {get; set;}
    
    [KoKu.JsonApi.Ignore]
    public string Demo {get; set;}
    
    [KoKu.JsonApi.Included]
    public List<Post> Posts {get; set;}
}
```

with

```c#
User user = new User()
{
    Id = 10,
    Name = "My name",
    Address = "My address",
    Demo = "Invisible to the user"
};

user.Posts = new List<Post>();
user.Posts.Add(new Post() {
    Id = 1,
    Message = "Wee haaa"
});
user.Posts.Add(new Post() {
    Id = 2,
    Message = "working ?"
});

// serialize to jsonApi.org format
var writeJsonApi = KoKu.JsonApi.Serializer.fromObject(user);
// deserilize from jsonApi.org format
var readJsonApi = KoKu.JsonApi.Deserializer.toObject(writeJsonApi);
// serialize again for fun..
return KoKu.JsonApi.Serializer.fromObject(readJsonApi);
```

results in

```json
{
  "data": [
    {
      "type": "user",
      "id": 10,
      "attributes": {
        "Name": "My name",
        "Address": "My address"
      },
      "relationships": {
        "Posts": {
          "data": [
            {
              "type": "post",
              "id": 1
            },
            {
              "type": "post",
              "id": 2
            }
          ]
        }
      }
    }
  ],
  "included": [
    {
      "type": "post",
      "id": 1,
      "attributes": {
        "Message": "Wee haaa"
      }
    },
    {
      "type": "post",
      "id": 2,
      "attributes": {
        "Message": "working ?"
      }
    }
  ]
}
```
