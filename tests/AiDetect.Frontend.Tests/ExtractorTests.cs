using AiDetect.Core.Features;
using AiDetect.Languages.Frontend;

namespace AiDetect.Frontend.Tests;

public class ExtractorTests
{
    internal const string AiReact = """
        import { useCallback, useEffect, useState } from 'react';
        import type { User } from '../types';

        interface UserListProps {
          /** Endpoint that returns the list of users. */
          endpoint: string;
          /** Called when a user is selected. */
          onSelect?: (user: User) => void;
        }

        type Status = 'idle' | 'loading' | 'error';

        /**
         * Renders a list of users fetched from the given endpoint.
         */
        export function UserList({ endpoint, onSelect }: UserListProps): JSX.Element {
          const [users, setUsers] = useState<User[]>([]);
          const [error, setError] = useState<string | null>(null);
          const [isLoading, setIsLoading] = useState(false);

          useEffect(() => {
            const controller = new AbortController();

            const loadUsers = async () => {
              setIsLoading(true);
              try {
                const response = await fetch(endpoint, { signal: controller.signal });
                if (!response.ok) {
                  throw new Error(`HTTP error! status: ${response.status}`);
                }
                const data: User[] = await response.json();
                setUsers(data);
              } catch (error: unknown) {
                if (error instanceof Error && error.name === 'AbortError') return;
                console.error('Failed to load users:', error);
                setError(error instanceof Error ? error.message : 'Unknown error');
              } finally {
                setIsLoading(false);
              }
            };

            loadUsers();
            return () => controller.abort();
          }, [endpoint]);

          const handleSelect = useCallback((user: User) => {
            onSelect?.(user);
          }, [onSelect]);

          if (isLoading) return <p role="status">Loading…</p>;
          if (error) return <p role="alert">{error}</p>;

          return (
            <ul aria-label="Users">
              {/* User items */}
              {users.map((user) => (
                <li key={user.id}>
                  <button type="button" onClick={() => handleSelect(user)} aria-label={`Select ${user.name}`}>
                    {user.name ?? 'Unnamed'}
                  </button>
                </li>
              ))}
            </ul>
          );
        }
        """;

    internal const string HumanReact = """
        import React from 'react'
        import axios from 'axios'

        // TODO: move to config
        var API = '/api/users'

        export default class UserList extends React.Component {
          state = { users: [], loading: false }

          componentDidMount() {
            this.setState({loading: true})
            axios.get(API).then(res => {
              this.setState({users: res.data, loading: false})
            }).catch(e => {
              console.log(e)
            })
          }

          render() {
            let list = this.state.users.map((u, i) => <li key={i} onClick={() => this.props.onSelect(u)}>{u.name || ''}</li>)
            // console.log(list)
            if(this.state.loading) return <div>loading...</div>
            return <div className="users" style={{padding: 10}}>
              <ul>{list}</ul>
              <button onClick={this.reload}>Reload</button>
            </div>
          }
        }
        """;

    private static CodeStats Extract(string path, string code, bool test = false) =>
        FrontendFeatureExtractor.Extract(path, code, _ => true, test, true);

    [Fact]
    public void AiStyleReactComponent()
    {
        var s = Extract("src/components/UserList.tsx", AiReact);
        Assert.Equal(1, s[FK.Components]);
        Assert.Equal(1, s[FK.Effects]);
        Assert.Equal(1, s[FK.EffectsWithResource]);
        Assert.Equal(1, s[FK.EffectsResourceCleaned]);
        Assert.Equal(1, s[FK.AbortControllers]);
        Assert.Equal(1, s[FK.ResponseOk]);
        Assert.True(s[FK.InstanceofMessage] >= 1);
        Assert.Equal(1, s[FK.ConsoleErrorTemplated]);
        Assert.Equal(1, s[FK.CatchParamError]);
        Assert.True(s[FK.FailedMessages] >= 1);
        Assert.True(s[FK.HttpErrorPhrase] >= 1);
        Assert.Equal(1, s[FK.Buttons]);
        Assert.Equal(1, s[FK.ButtonsTyped]);
        Assert.True(s[FK.AriaAttrs] >= 2);
        Assert.Equal(1, s[FK.JsxSection]);
        Assert.Equal(2, s[FK.PropsMembers]);
        Assert.Equal(2, s[FK.PropsMembersDocumented]);
        Assert.Equal(1, s[FK.DocDocumented]);
        Assert.Equal(1, s[FK.JsDocTemplate]);
        Assert.Equal(1, s[FK.LiteralUnions]);
        Assert.True(s[FK.UnknownTypes] >= 1);
        Assert.Equal(1, s[FK.PropsTypeNamed]);
        Assert.Equal(1, s[FK.OptionalCall]);
        Assert.True(s[FK.Nullish] >= 1);
        Assert.Equal(1, s[FK.ImportTypes]);
        Assert.Equal(2, s[FK.StateHooksGeneric]);
        Assert.Equal(0, s[FK.ConsoleLogs]);
        Assert.Equal(0, s[FK.KeyIndex]);
        Assert.Equal(1, s[FK.ArrayCallbackDescriptive]);
    }

    [Fact]
    public void HumanStyleReactComponent()
    {
        var s = Extract("src/components/UserList.js", HumanReact);
        Assert.Equal(1, s[FK.ClassComponents]);
        Assert.Equal(1, s[FK.Todo]);
        Assert.Equal(1, s[FK.CommentedOutCode]);
        Assert.Equal(1, s[FK.ConsoleLogs]);
        Assert.Equal(1, s[FK.CatchLogOnly]);
        Assert.Equal(1, s[FK.CatchParamShort]);
        Assert.Equal(1, s[FK.ThenCalls]);
        Assert.Equal(1, s[FK.VarDecl]);
        Assert.Equal(1, s[FK.KeyIndex]);
        Assert.Equal(1, s[FK.InlineStyles]);
        Assert.Equal(1, s[FK.ClickOnDiv]);
        Assert.Equal(1, s[FK.Buttons]);
        Assert.Equal(0, s[FK.ButtonsTyped]);
        Assert.Equal(1, s[FK.ArrayCallbackSingleLetter]);
        Assert.True(s[FK.OrDefault] >= 1);
        Assert.True(s[FK.KeywordNoSpace] >= 1);
        Assert.True(s[FK.MissingSemicolons] >= 3);
    }

    [Fact]
    public void ScopeLimitsCountingToAddedLines()
    {
        // Только строка с console.error в области анализа.
        int line = AiReact.Split('\n').ToList().FindIndex(l => l.Contains("console.error", StringComparison.Ordinal)) + 1;
        var s = FrontendFeatureExtractor.Extract("a.tsx", AiReact, l => l == line, false, false);
        Assert.Equal(1, s[FK.ConsoleErrorTemplated]);
        Assert.Equal(0, s[FK.Buttons]);
        Assert.Equal(0, s[FK.ResponseOk]);
        Assert.Equal(1, s[FK.CodeLines]);
    }

    [Fact]
    public void AngularComponentTeardown()
    {
        var good = """
            @Component({ selector: 'app-users', templateUrl: './users.component.html', changeDetection: ChangeDetectionStrategy.OnPush })
            export class UsersComponent implements OnInit, OnDestroy {
              private readonly destroy$ = new Subject<void>();
              users: User[] = [];

              constructor(private readonly userService: UserService, private readonly store: Store) {}

              ngOnInit(): void {
                this.store.select(selectUsers).pipe(takeUntil(this.destroy$)).subscribe({
                  next: (users) => (this.users = users),
                  error: (error: unknown) => console.error('Failed to load users:', error),
                });
              }

              ngOnDestroy(): void {
                this.destroy$.next();
                this.destroy$.complete();
              }
            }
            """;
        var s = Extract("src/app/users/users.component.ts", good);
        Assert.Equal(1, s[FK.NgComponents]);
        Assert.Equal(2, s[FK.NgCtorParams]);
        Assert.Equal(2, s[FK.NgCtorReadonly]);
        Assert.Equal(1, s[FK.Subscribes]);
        Assert.Equal(1, s[FK.SubscribesTornDown]);
        Assert.Equal(1, s[FK.NgObserverSubscribe]);
        Assert.Equal(1, s[FK.DestroySubjects]);
        Assert.Equal(1, s[FK.NgOnPush]);

        var bad = """
            @Component({ selector: 'app-users', templateUrl: './users.component.html' })
            export class UsersComponent implements OnInit {
              users: any;
              constructor(private store: Store, private route: ActivatedRoute) {}
              ngOnInit() {
                this.route.params.subscribe(p => {
                  this.store.select(selectUser(p.id)).subscribe(u => this.users = u);
                });
              }
            }
            """;
        var h = Extract("src/app/users/users.component.ts", bad);
        Assert.Equal(2, h[FK.NgCtorParams]);
        Assert.Equal(0, h[FK.NgCtorReadonly]);
        Assert.Equal(2, h[FK.Subscribes]);
        Assert.Equal(0, h[FK.SubscribesTornDown]);
        Assert.Equal(1, h[FK.NgNestedSubscribe]);
        Assert.Equal(1, h[FK.AnyTypes]);
    }

    [Fact]
    public void AngularTemplate()
    {
        var html = """
            <!-- Users list -->
            <ul>
              <li *ngFor="let user of users$ | async; trackBy: trackById">
                <button type="button" (click)="handleSelect(user)" [attr.aria-label]="user.name">{{ user.name }}</button>
              </li>
            </ul>
            <div (click)="close()">x</div>
            """;
        var s = Extract("src/app/users/users.component.html", html);
        Assert.Equal(1, s[FK.NgFor]);
        Assert.Equal(1, s[FK.NgForTrackBy]);
        Assert.Equal(1, s[FK.AsyncPipes]);
        Assert.Equal(1, s[FK.ButtonsTyped]);
        Assert.Equal(1, s[FK.AriaAttrs]);
        Assert.Equal(1, s[FK.MarkupSection]);
        Assert.Equal(1, s[FK.ClickOnDiv]);
        Assert.Equal(1, s[FK.HandlerAttrsHandle]);
    }

    [Fact]
    public void VueSingleFileComponent()
    {
        var sfc = """
            <template>
              <ul>
                <li v-for="(item, index) in items" :key="index" v-if="item.visible">{{ item.name }}</li>
              </ul>
            </template>

            <script setup lang="ts">
            import { onUnmounted, ref } from 'vue';

            interface Props {
              items: Item[];
            }

            const props = withDefaults(defineProps<Props>(), { items: () => [] });
            const emit = defineEmits<{ (e: 'select', id: string): void }>();
            const timer = setInterval(() => emit('select', 'x'), 1000);
            onUnmounted(() => clearInterval(timer));
            </script>

            <style scoped lang="scss">
            /* List styles */
            .list { color: var(--text-color); transition: color 0.2s ease; }
            </style>
            """;
        var s = Extract("src/components/List.vue", sfc);
        Assert.Equal(1, s[FK.VueScriptSetup]);
        Assert.Equal(1, s[FK.VueLangTs]);
        Assert.Equal(1, s[FK.VuePropsTyped]);
        Assert.Equal(1, s[FK.VueWithDefaults]);
        Assert.Equal(1, s[FK.VueEmitsTyped]);
        Assert.Equal(1, s[FK.VueUnmountHooks]);
        Assert.Equal(1, s[FK.VueFor]);
        Assert.Equal(1, s[FK.VueForKeyIndex]);
        Assert.Equal(1, s[FK.VueIfWithFor]);
        Assert.Equal(1, s[FK.VueScopedStyles]);
        Assert.Equal(1, s[FK.CssSection]);
        Assert.Equal(1, s[FK.CssVars]);
        Assert.Equal(1, s[FK.CssLlmValues]);
        Assert.True(s[FK.MarkupLines] >= 2 && s[FK.ScriptLines] >= 8 && s[FK.StyleLines] >= 1);
        Assert.Equal(22, s.GetExamples(FK.CssLlmValues)[0].Line);
    }

    [Fact]
    public void JestTestsStyle()
    {
        var test = """
            import { render, screen } from '@testing-library/react';
            import userEvent from '@testing-library/user-event';

            describe('UserList', () => {
              beforeEach(() => {
                jest.clearAllMocks();
              });

              it('should render users', async () => {
                // Arrange
                const fetchMock = jest.fn().mockResolvedValue({ ok: true, json: async () => [{ id: '1', name: 'John Doe' }] });
                // Act
                render(<UserList endpoint="/api/users" />);
                // Assert
                expect(await screen.findByRole('button', { name: /john doe/i })).toBeInTheDocument();
                expect(fetchMock).toHaveBeenCalledWith('/api/users', expect.anything());
              });

              it.each([['a'], ['b']])('should handle %s', async (name) => {
                await userEvent.click(screen.getByRole('button'));
                expect(screen.getByText(name)).toBeTruthy();
              });
            });
            """;
        var s = Extract("src/components/UserList.test.tsx", test, test: true);
        Assert.Equal(1, s[FK.TestCases]);
        Assert.Equal(1, s[FK.TestShould]);
        Assert.Equal(1, s[FK.TestEach]);
        Assert.Equal(1, s[FK.TestClearMocks]);
        Assert.Equal(1, s[FK.TestFilesWithMocks]);
        Assert.Equal(2, s[FK.RtlRoleQueries]);
        Assert.Equal(1, s[FK.UserEvent]);
        Assert.Equal(3, s[FK.AaaComments]);
        Assert.True(s[FK.ExampleData] >= 1);
        Assert.True(s[FK.StrongAsserts] >= 1);
        Assert.Equal(1, s[FK.WeakAsserts]);
    }

    [Fact]
    public void CommentsClassification()
    {
        var code = """
            // Get the current user
            const user = getUser();
            // проверяем права, потом переделать
            if (user.isAdmin) {
              // const old = legacy(user);
            }
            //quick hack
            // Получаем список заказов
            const orders = loadOrders();
            """;
        var s = Extract("a.ts", code);
        Assert.Equal(5, s[FK.LineComments]);
        Assert.Equal(2, s[FK.Narrative]);
        Assert.Equal(1, s[FK.CommentedOutCode]);
        Assert.True(s[FK.Casual] >= 2);
        Assert.Equal(2, s[FK.CyrillicComments]);
        Assert.Equal(1, s[FK.Restating]);
    }

    [Fact]
    public void NamingTranslitAndTypos()
    {
        var code = """
            const spisokZakazov = [];
            const recieveData = () => null;
            const заказ = 1;
            """;
        var s = Extract("a.js", code);
        Assert.Equal(1, s[FK.Translit]);
        Assert.Equal(1, s[FK.Typos]);
        Assert.Equal(1, s[FK.CyrillicIdentifiers]);
    }
}
